# Architecture

Design for a Testcontainers-style .NET library with pluggable container backends: `Purview.Containers`.

## Core model

The library is split into a backend-neutral abstraction assembly and one assembly per backend:

```
Purview.Containers                       (net10.0, portable)
 ├─ IContainer / IContainerConfiguration / ContainerConfiguration   the container contract
 ├─ ContainerBuilder<TBuilder, TContainer, TConfiguration>          fluent configuration + validation
 ├─ ContainerBase                                                   typed module container (delegates to the backend)
 ├─ ContainerBackends + IContainerBackend                           backend registry and selection
 ├─ Waiting / Images / Mounts / Networking / Diagnostics            readiness, model, secrets
 └─ Runtime/ContainerException                                      neutral error taxonomy

Purview.Containers.Wsl                   (net10.0 facade + net10.0-windows10.0.19041.0 implementation)
 ├─ WslContainerBackend : IContainerBackend  registers itself as "wsl"
 ├─ WslContainerRuntime : IContainerRuntime  process singleton, owns the shared session
 │   ├─ SessionSettings (name, storagePath, cpu/mem, gpu, timeout)
 │   ├─ SemaphoreSlim   -> serialises session-mutating and container-lifecycle ops
 │   ├─ ImageCatalog    -> pull policies + keyed dedup of concurrent pulls
 │   ├─ PortAllocator   -> native random host ports (windowsPort=0)
 │   └─ SessionHandle   -> Microsoft.WSL.Containers.Session (internal)
 ├─ WslContainer        : IContainer         WSLC-backed container
 └─ WslContainerSession : IContainerSession  image pull + container create/start/stop/delete/exec

IContainer : IAsyncDisposable
  StartAsync / StopAsync / DisposeAsync / ExecAsync / GetMappedPublicPort /
  GetLogsAsync / tailing IAsyncEnumerable
```

A module (`Purview.Containers.PostgreSql`, `Purview.Containers.Redis`, …) derives its container from
`ContainerBase` and references `Purview.Containers` only. The backend is resolved at run time through
`ContainerBackends`, so the same module package works on WSLC or Docker.

Microsoft types (`Session`, `Container`, `Process`, `ContainerSettings`, …) are **runtime implementation details** kept behind the public interfaces. They are not exposed through the public API surface (an opt-in accessor is the only escape hatch).

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
`Purview.Containers.targets`; project-reference consumers register explicitly with
`Register(...)`. Consumer-facing guidance (including the CI example) is in
[Backends: WSLC or Docker](Backends.md).

## Session lifetime model (chosen after spikes)

**One shared, process-wide session.** Findings that drove this:

- Session start is cheap (~20-30 ms) because the WSL VM is shared under the session manager. (EXP)
- The image store is keyed by the **storage path** (EXP, S2): a stable storage path gives a warm image cache across sessions and process restarts.
- Per-container sessions would force a fresh storage path per container → a cold image store (re-pull) per container, which is the dominant cost (alpine ~2-3 s, python ~3-4 s). Not acceptable for test throughput.
- `Session`/`Container` lifecycle calls are **not reliably thread-safe**: a concurrent `Container.Start()` occasionally fails with `0x8000FFFF` (E_UNEXPECTED) (EXP, S8). Container isolation is achieved by unique container names + unique host ports, not separate sessions.

Design rules:

1. **One lazily-started session per process** with a deterministic name `wslc-{processId}-{8 hex}` (unique per machine; session names are reserved until the session is disposed — EXP S1/S14) and a **stable storage path** (default `%LOCALAPPDATA%\Purview.WslContainers\sessions\{name}\`), configurable.
2. The session VM is capped at **4096 MB by default** (`WslContainerRuntimeOptions.Default`). This is required for SQL Server (which refuses to start below 2000 MB — `sqlservr: This program requires a machine with at least 2000 megabytes of memory`) and harmless for lighter containers. Override via `WslContainerRuntimeOptions.MemorySizeInMB`.
2. `IContainerBackend` is the seam for backends: a backend package (starting with `Purview.Containers.Wsl`) supplies `IContainer` instances, and `ContainerBackends` resolves which one runs. `IContainerRuntime` remains the WSLC-internal seam so advanced users/tests can substitute a per-container-session runtime for isolation experiments.
3. Container `DisposeAsync` never terminates the shared session; it deletes the container only.
4. The runtime registers a **process-exit handler** and `IAsyncDisposable` to `Terminate()`+`Dispose()` the session at shutdown.

## Session naming and storage

- Name: `wslc-{pid}-{random8}`. Never place secrets/credentials in names, paths, or logs.
- **Storage is shared by default** (`StorageMode.Shared`): all sessions use
  `%LOCALAPPDATA%\Purview.WslContainers\images`, so the image store is pulled once and reused across
  process runs. Set `StorageMode.PerSession` (or an explicit `StoragePath` / `PURVIEW_CONTAINERS_STORAGE_PATH`)
  for isolation. Session names stay unique per process; only the path is shared.
- **Concurrent sharing is not possible**: a running session exclusively locks its `storage.vhdx`
  (a second session on the same path fails with `0x80070020` — EXP S18). The lock is taken lazily on the
  first store access, so contention can surface on `GetImages()` rather than at session start; the runtime
  detects it there too and falls back to an isolated per-process store.
  Sequential reuse works (EXP S2/S13): after a session ends, a new session on the same path sees its images.
- Cleanup: normal shutdown terminates+disposes the session (frees the name, EXP S14). Orphaned sessions from crashed processes block only their own name (EXP S13); they do not block other sessions or storage-path reuse.

## Image management

- `PullPolicy { Missing, Always, Never }`, default `Missing`.
- `EnsureImageAsync`: consult `GetImages()` (session store) → pull when missing.
- Concurrent pulls of the same image are **deduplicated** by a keyed async lock (WSLC does not dedupe; EXP S11).
- Registry auth via `Session.Authenticate(uri, user, pass)` → `AuthenticateResult.IdentityToken` → `PullImageOptions.RegistryAuth`. Credentials live in a `Secret` type; never logged or serialized.

## Ports

- `PortBinding(containerPort, hostPort?, protocol, hostAddress?)`.
- **Random host port = native `windowsPort=0`** (race-free, EXP S4). The assigned port is read from `Inspect().Ports`.
- Explicit host ports are bound by WSLC at `Start`; a conflict throws `0x80072740` (surface as a clear `WslContainerPortInUseException`).
- Default host bind is IPv4 loopback only; IPv6 is not mapped (EXP S12).
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
`StoragePath`/`PURVIEW_CONTAINERS_STORAGE_PATH`/`StorageMode.PerSession` configuration opts out of the
fallback. Isolated stores are transient and are removed when their session terminates.

## Cleanup & reaper decision

**No Ryuk-style sidecar process is needed.**

- `Session.Dispose()` removes the session from the manager and frees its name (EXP S14).
- The library registers a process-exit handler so the session is always disposed on normal termination.
- Crash residue: an orphaned session from a dead process remains registered with a dead `Creator PID` (EXP S10). It reserves only its own name. It can be swept with `wslc --session <name> system session terminate`; the library documents this as an optional maintenance step and may offer a dev-time sweep utility (CLI-based, clearly isolated from the core runtime).
- `Container.DisposeAsync` is idempotent: stop (SIGTERM→SIGKILL) then `Delete(Force)`; swallow `RPC_E_DISCONNECTED`/`ContainerNotFound` on double-delete (EXP S7).

## Observability

- `System.Diagnostics.ActivitySource("Purview.Containers")` emits `wslcontainer.session.start`, `wslcontainer.image.pull`, `wslcontainer.container.create/start/stop/delete`, `wslcontainer.exec.create`, and `wslcontainer.wait`. Tags carry container name/id/image and strategy — never credentials.
- Tailing `GetLogsAsync(CancellationToken)` ends when the container's init process exits: the log buffer is flushed and its channel completed on process exit (and on container disposal), so an `await foreach` over the stream terminates instead of waiting forever. The string overload (`GetLogsAsync(stream, ct)`) reads the accumulated buffer only.
- `Microsoft.Extensions.Logging` integration is optional/future; the core works without a host or DI.

## Extensibility

- Public surface deliberately small; WSLC types internal.
- One opt-in accessor (`IWslContainerAccessor`) exposes `Inspect()` / raw handle for advanced users.
- Third-party modules subclass the generic builder (see [Modules](Modules.md)).