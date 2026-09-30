# Architecture

Design for a WSLC-native Testcontainers-style .NET library: `Purview.WslContainers`.

## Core model

```
WslContainerRuntime (process singleton, IAsyncDisposable)
 ├─ SessionSettings (name, storagePath, cpu/mem, gpu, timeout)
 ├─ SemaphoreSlim        -> serialises session-mutating and container-lifecycle ops
 ├─ ImageCatalog         -> pull policies + keyed dedup of concurrent pulls
 ├─ PortAllocator        -> native random host ports (windowsPort=0)
 └─ SessionHandle        -> Microsoft.WSL.Containers.Session (internal)

IContainerRuntime
  Task<WslContainerRuntimeInfo> GetInfoAsync(CancellationToken ct = default)
  Task<IContainerSession> GetSessionAsync(CancellationToken ct = default)
  Task InitializeAsync(...)

IContainer : IAsyncDisposable
  StartAsync / StopAsync / DisposeAsync / ExecAsync / GetMappedPublicPort /
  GetLogsAsync / tailing IAsyncEnumerable
```

Microsoft types (`Session`, `Container`, `Process`, `ContainerSettings`, …) are **runtime implementation details** kept behind the public interfaces. They are not exposed through the public API surface (an opt-in accessor is the only escape hatch).

## Session lifetime model (chosen after spikes)

**One shared, process-wide session.** Findings that drove this:

- Session start is cheap (~20-30 ms) because the WSL VM is shared under the session manager. (EXP)
- The image store is keyed by the **storage path** (EXP, S2): a stable storage path gives a warm image cache across sessions and process restarts.
- Per-container sessions would force a fresh storage path per container → a cold image store (re-pull) per container, which is the dominant cost (alpine ~2-3 s, python ~3-4 s). Not acceptable for test throughput.
- `Session`/`Container` lifecycle calls are **not reliably thread-safe**: a concurrent `Container.Start()` occasionally fails with `0x8000FFFF` (E_UNEXPECTED) (EXP, S8). Container isolation is achieved by unique container names + unique host ports, not separate sessions.

Design rules:

1. **One lazily-started session per process** with a deterministic name `wslc-{processId}-{8 hex}` (unique per machine; session names are reserved until the session is disposed — EXP S1/S14) and a **stable storage path** (default `%LOCALAPPDATA%\Purview.WslContainers\sessions\{name}\`), configurable.
2. The session VM is capped at **4096 MB by default** (`WslContainerRuntimeOptions.Default`). This is required for SQL Server (which refuses to start below 2000 MB — `sqlservr: This program requires a machine with at least 2000 megabytes of memory`) and harmless for lighter containers. Override via `WslContainerRuntimeOptions.MemorySizeInMB`.
2. `IContainerRuntime` is the seam so advanced users/tests can substitute a per-container-session runtime for isolation experiments.
3. Container `DisposeAsync` never terminates the shared session; it deletes the container only.
4. The runtime registers a **process-exit handler** and `IAsyncDisposable` to `Terminate()`+`Dispose()` the session at shutdown.

## Session naming and storage

- Name: `wslc-{pid}-{random8}`. Never place secrets/credentials in names, paths, or logs.
- **Storage is shared by default** (`StorageMode.Shared`): all sessions use
  `%LOCALAPPDATA%\Purview.WslContainers\images`, so the image store is pulled once and reused across
  process runs. Set `StorageMode.PerSession` (or an explicit `StoragePath` / `WSL_CONTAINERS_STORAGE_PATH`)
  for isolation. Session names stay unique per process; only the path is shared.
- **Concurrent sharing is not possible**: a running session exclusively locks its `storage.vhdx`
  (a second session on the same path fails with `0x80070020` — EXP S18). When the default shared store is
  contended by a concurrent process, the runtime automatically falls back to an isolated per-process store.
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
- UDP → `WslContainerNotSupportedException`.

## Concurrency

- All session-mutating operations (`Pull`, `CreateContainer`, `Start`, `Stop`, `Delete`, `Tag`, `DeleteImage`, VHD ops) go through a per-session `SemaphoreSlim`. This avoids the racy `0x8000FFFF` observed with concurrent `Start`.
- Read-only ops (`Inspect`, `GetImages`, `Exec`?) are allowed concurrently once the container is running; `Exec` still funnels through the lock because it creates a process on the container.
- Bounded per-container output buffers prevent runaway memory.

## Cleanup & reaper decision

**No Ryuk-style sidecar process is needed.**

- `Session.Dispose()` removes the session from the manager and frees its name (EXP S14).
- The library registers a process-exit handler so the session is always disposed on normal termination.
- Crash residue: an orphaned session from a dead process remains registered with a dead `Creator PID` (EXP S10). It reserves only its own name. It can be swept with `wslc --session <name> system session terminate`; the library documents this as an optional maintenance step and may offer a dev-time sweep utility (CLI-based, clearly isolated from the core runtime).
- `Container.DisposeAsync` is idempotent: stop (SIGTERM→SIGKILL) then `Delete(Force)`; swallow `RPC_E_DISCONNECTED`/`ContainerNotFound` on double-delete (EXP S7).

## Observability

- `System.Diagnostics.ActivitySource("Purview.WslContainers")` emits `wslcontainer.session.start`, `wslcontainer.image.pull`, `wslcontainer.container.create/start/stop/delete`, `wslcontainer.exec.create`, and `wslcontainer.wait`. Tags carry container name/id/image and strategy — never credentials.
- `Microsoft.Extensions.Logging` integration is optional/future; the core works without a host or DI.

## Extensibility

- Public surface deliberately small; WSLC types internal.
- One opt-in accessor (`IWslContainerAccessor`) exposes `Inspect()` / raw handle for advanced users.
- Third-party modules subclass the generic builder (see `docs/modules.md`).