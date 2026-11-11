# Architecture

Design for a Testcontainers-style .NET library with pluggable container backends: `Purview.Containers`.

## Core model

The library is split into a backend-neutral abstraction assembly and one assembly per backend:

```
Purview.Containers                       (net10.0 + net11.0)   umbrella: references Core, Wsl and Docker
 └─ Purview.Containers.Core              (net10.0 + net11.0, portable)   the backend-neutral abstractions
     ├─ IContainer / IContainerConfiguration / ContainerConfiguration   the container contract
     ├─ ContainerBuilder<TBuilder, TContainer, TConfiguration>          fluent configuration + validation
     ├─ ContainerBase                                                   typed module container (delegates to the backend)
     ├─ ContainerBackends + IContainerBackend                           backend registry and selection
     ├─ Waiting / Images / Mounts / Networking / Diagnostics            readiness, model, secrets
     ├─ IConnectionStringProvider / ConnectionMode / ContainerConnectionStringProvider   connection strings
     └─ Runtime/ContainerException                                      neutral error taxonomy

Purview.Containers.Wsl                   (net10.0 + net11.0 facades,
                                          net10.0 + net11.0 windows10.0.19041.0 implementations)
 ├─ WslContainerBackend : IContainerBackend  registers itself as "wsl"
 ├─ WslContainerRuntime : IContainerRuntime  process singleton, owns the shared session
 │   ├─ SessionSettings (name, storagePath, cpu/mem, gpu, timeout)
 │   ├─ SemaphoreSlim   -> serialises session-mutating and container-lifecycle ops
 │   ├─ ImageCatalog    -> pull policies + keyed dedup of concurrent pulls
 │   ├─ PortAllocator   -> native random host ports (windowsPort=0)
 │   └─ SessionHandle   -> Microsoft.WSL.Containers.Session (internal)
 ├─ WslContainer        : IContainer         WSLC-backed container
 ├─ WslContainerSession : IContainerSession  image pull + container create/start/stop/delete/exec
 └─ WslResource / WslCommand / WslFileCopy  owned resources; bounded CLI fallback in that same session

IContainer : IConnectionStringProvider, IAsyncDisposable
  StartAsync / StopAsync / DisposeAsync / ExecAsync / GetMappedPublicPort /
  GetConnectionString / GetLogsAsync / tailing IAsyncEnumerable
  CopyAsync / ReadFileAsync / WaitForExitAsync / DeleteAsync
```

`IContainerBackend.CreateNetworkAsync` and `BuildImageAsync` return owned `IContainerResource`
instances on either backend. Docker delegates to Testcontainers. WSLC networks and their deletion use the
managed SDK; CLI-backed resources (image builds) target the SDK-owned session/store, while lifecycle,
images and network operations remain SDK-backed. Dispose containers before networks/images. See
[Capabilities and Cancellation](Backend-Capabilities.md).

A module (`Purview.Containers.PostgreSql`, `Purview.Containers.Redis`, …) derives its container from
`ContainerBase` and references `Purview.Containers.Core` only. The backend is resolved at run time through
`ContainerBackends`, so the same module package works on WSLC or Docker. `Purview.Containers` is the
umbrella package: it carries no code of its own and simply references `Core` plus both backends, so one
reference gives a consumer the whole "auto" setup.

Microsoft types (`Session`, `Container`, `Process`, `ContainerSettings`, …) are **runtime implementation details** kept behind the public interfaces. They are not exposed through the public API surface (an opt-in accessor is the only escape hatch).

## Target frameworks

Every package ships `net10.0` and `net11.0` assets. **.NET 8 and .NET 9 are dropped**; the contract is in
[Consumer Requirements](Consumer-Requirements.md). Purview.BuildSdk's `Current` target-framework set
supplies that pair; the repository does not hand-maintain it.

- `net10.0` is the floor. A consumer that has not moved to .NET 11 keeps the classic compiler-generated
  async state machines and the .NET 10 library surface.
- `net11.0` is compiled with `runtime-async=on` and the C# 15 updated memory-safety rules
  (`MemorySafetyRules=2`). The runtime manages suspension and resumption instead of compiler-generated
  state machines, which lowers async overhead and makes live stack traces (profiler, `new StackTrace()`,
  debugger) name the real methods. `PurviewRuntimeAsync=false` opts out for a local experiment;
  `Wsl.UnitTests/BuildFeatureTests.cs` guards that the shipped default is on.
- The WSL Containers backend uses the same split for its Windows builds, and a single implementation
  payload serves both portable targets because a .NET 11 process loads a .NET 10 assembly.

C# 15 `closed` hierarchies and `union` types are deliberately **not** used. Both need .NET 11 runtime types
(`IsClosedTypeAttribute`, `UnionAttribute`), so they could only exist in the `net11.0` assets and would
split the public API by target framework. No type here is a closed hierarchy in the language sense either:
the wait strategies, `ContainerException` and the backend interfaces are documented extension points, and
the service modules derive from `ContainerBase` in their own assemblies.

## Backend selection

`ContainerBackends` is the process-wide registry; `ResolveAsync()` chooses the backend with this
precedence:

1. **Pinned instance** — `ContainerBackends.Use(new WslContainerBackend())`, or `WithBackend(...)` on a
   single builder. Used as-is: never probed, never substituted.
2. **Named backend** — `PURVIEW_CONTAINERS_BACKEND=wsl|docker|<name>` (or
   `Use(ContainerBackendSelection.Named(...))`). The named backend is probed: an unknown name lists what is
   registered, and an unusable one fails with its own diagnostics. There is no fallback.
3. **Automatic detection** — every registered backend is probed in **auto-priority** order and the first
   *available and compatible* one wins. A backend positions itself with `IContainerBackendPreference`
   (lower `AutoPriority` first; a backend without it counts as 0, so registration order is preserved for
   ties). The WSL Containers backend declares a lower priority than Docker, so a machine that can run both
   prefers WSLC. When none is usable the exception lists each backend's availability, version and missing
   components, plus the `PURVIEW_CONTAINERS_BACKEND` values that would work.

Resolution is cached per process, so probes run once; `Reset()` (tests) and `Register`/`Use` invalidate the
cache. Package consumers get registration from the generated module initializer in
`Purview.Containers.Core.targets`; project-reference consumers register explicitly with
`Register(...)`. Consumer-facing guidance (including the CI example) is in
[Backends: WSLC or Docker](Backends.md).

## Session lifetime model

**One shared, process-wide session.** Findings that drove this:

- Session start is cheap (~20-30 ms) because the WSL VM is shared under the session manager.
- The image store is keyed by the **storage path**: a stable storage path gives a warm image cache across sessions and process restarts.
- Per-container sessions would force a fresh storage path per container → a cold image store (re-pull) per container, which is the dominant cost (alpine ~2-3 s, python ~3-4 s). Not acceptable for test throughput.
- `Session`/`Container` lifecycle calls are **not reliably thread-safe**: a concurrent `Container.Start()` occasionally fails with `0x8000FFFF` (E_UNEXPECTED). Container isolation is achieved by unique container names + unique host ports, not separate sessions.

Design rules:

1. **One lazily-started session per process** with a deterministic name `purview-{processId}-{8 hex}` (unique per machine; session names are reserved until the session is disposed) and a **stable storage path** (default `%LOCALAPPDATA%\Purview\WslContainers\images`), configurable via `WslContainerRuntimeOptions.StoragePath` or `PURVIEW_CONTAINERS_STORAGE_PATH`.
2. The session VM is capped at **4096 MB by default** (`WslContainerRuntimeOptions.Default`). This is required for SQL Server (which refuses to start below 2000 MB — `sqlservr: This program requires a machine with at least 2000 megabytes of memory`) and harmless for lighter containers. Override via `WslContainerRuntimeOptions.MemorySizeInMB`.
2. `IContainerBackend` is the seam for backends: a backend package (starting with `Purview.Containers.Wsl`) supplies `IContainer` instances, and `ContainerBackends` resolves which one runs. `IContainerRuntime` remains the WSLC-internal seam so advanced users/tests can substitute a per-container-session runtime for isolation experiments.
3. Container `DisposeAsync` never terminates the shared session; it deletes the container only.
4. The runtime registers a **process-exit handler** and `IAsyncDisposable` to `Terminate()`+`Dispose()` the session at shutdown.

## Session naming and storage

- Name: `purview-{pid}-{random8}`. Never place secrets/credentials in names, paths, or logs.
- **Storage is shared by default** (`StorageMode.Shared`): all sessions use
  `%LOCALAPPDATA%\Purview\WslContainers\images`, so the image store is pulled once and reused across
  process runs. Set `StorageMode.PerSession` (or an explicit `StoragePath` /
  `PURVIEW_CONTAINERS_STORAGE_PATH`) for isolation; in code, configure the backend with
  `new WslContainerBackend(new WslContainerRuntimeOptions { StoragePath = … })`. Session names stay unique
  per process; only the path is shared.
- **Concurrent sharing is not possible**: a running session exclusively locks its `storage.vhdx`
  (a second session on the same path fails with `0x80070020`). The lock is taken lazily on the
  first store access, so contention can surface on `GetImages()` rather than at session start; the runtime
  detects it there too and falls back to an isolated per-process store.
  Sequential reuse works: after a session ends, a new session on the same path sees its images.
- Cleanup: normal shutdown terminates+disposes the session (frees the name). Orphaned sessions from crashed processes block only their own name, not other sessions or storage-path reuse. They are swept automatically before the next session starts (the runtime terminates `purview-*` and legacy `wslc-*` sessions whose creator PID is gone).

## Image management

- `PullPolicy { Missing, Always, Never }`, default `Missing`.
- `EnsureImageAsync`: consult `GetImages()` (session store) → pull when missing.
- Concurrent pulls of the same image are **deduplicated** by a keyed async lock (WSLC does not dedupe).
- Registry auth via `Session.Authenticate(uri, user, pass)` → `AuthenticateResult.IdentityToken` → `PullImageOptions.RegistryAuth`. Credentials live in a `Secret` type; never logged or serialized.

## Ports

- `PortBinding(containerPort, hostPort?, protocol, hostAddress?)`.
- **Random host port = native `windowsPort=0`** (race-free). The assigned port is read from `Inspect().Ports`.
- Explicit host ports are bound by WSLC at `Start`; a conflict throws `0x80072740` (surface as a clear `WslContainerPortInUseException`).
- Default host bind is IPv4 loopback only; IPv6 is not mapped.
- UDP → `ContainerNotSupportedException`.

## Concurrency

- All session-mutating operations (`Pull`, `CreateContainer`, `Start`, `Stop`, `Delete`, `Tag`, `DeleteImage`, VHD ops) go through a per-session `SemaphoreSlim`. This avoids the racy `0x8000FFFF` observed with concurrent `Start`.
- `GetImages()` reads (and locks) `storage.vhdx`, so it is **not** side-effect-free and goes through the same gate. Container-handle reads (`Inspect`) are allowed concurrently once the container is running; `Exec` also funnels through the lock because it creates a process on the container.
- Bounded per-container output buffers prevent runaway memory.

### Shared-store contention (cross-process)

`storage.vhdx` is opened lazily on the **first store access**, not at session start, so a second process
on the same path can start its session successfully and only fail later on its first read
(`GetImages()`) with `0x80070020`. The runtime therefore verifies the store once, under a gate, on the
first `GetSessionAsync`; when a concurrent process holds the default shared store, that session is
discarded and the runtime transparently switches to an isolated per-process store. Explicit
`StoragePath`/`PURVIEW_CONTAINERS_STORAGE_PATH`/`StorageMode.PerSession` configuration (or
`new WslContainerBackend(new WslContainerRuntimeOptions { StoragePath = … })`) opts out of the fallback.
Isolated stores are transient and are removed when their session terminates.

## Cleanup & reaper decision

**No Ryuk-style sidecar process is needed.**

- `Session.Dispose()` removes the session from the manager and frees its name.
- The library registers a process-exit handler so the session is always disposed on normal termination.
- Crash residue: an orphaned session from a dead process remains registered with a dead `Creator PID`. The runtime sweeps `purview-*` (and legacy `wslc-*`) sessions whose creator PID is gone before the first session starts, removes the matching per-session storage directories, removes stale containers whose generated name embeds a dead PID, and prunes unused anonymous volumes. `WslContainerRuntime.SweepOrphanedSessionsAsync()` runs the session sweep explicitly and `CleanUpAsync()` also removes container residue; neither touches a live session.
- `Container.DisposeAsync` is idempotent: stop (SIGTERM→SIGKILL) then `Delete(Force)`; swallow `RPC_E_DISCONNECTED`/`ContainerNotFound` on double-delete.

## Observability

- `System.Diagnostics.ActivitySource("Purview.Containers")` emits `wslcontainer.session.start`, `wslcontainer.image.pull`, `wslcontainer.container.create/start/stop/delete`, `wslcontainer.exec.create`, and `wslcontainer.wait`. Tags carry container name/id/image and strategy — never credentials.
- Tailing `GetLogsAsync(CancellationToken)` polls output until live container state reports exit; it does
  not depend solely on native exit events. The snapshot overload uses the SDK buffer or CLI logs for
  CLI-created containers. Always supply an enumeration token.
- `Microsoft.Extensions.Logging` integration is optional/future; the core works without a host or DI.

## Extensibility

- Public surface deliberately small; WSLC types internal.
- One opt-in accessor (`IWslContainerAccessor`) exposes `Inspect()` / raw handle for advanced users.
- Third-party modules subclass the generic builder (see [Modules](Modules.md)).