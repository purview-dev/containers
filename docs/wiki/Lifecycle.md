# Lifecycle

Container lifecycle semantics for `Purview.Containers`.

## Public API

```csharp
public interface IContainer : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task DeleteAsync(CancellationToken ct = default);
    Task<long> WaitForExitAsync(CancellationToken ct = default);
    ValueTask DisposeAsync();
    // restart is not part of this shared contract
}
```

## State model

WSLC `ContainerState`: `Invalid → Created → Running → Exited → Deleted`.

| Operation | Behaviour |
|---|---|
| `StartAsync` | Creates the session if needed, ensures image (per pull policy), `CreateContainer`, then `Container.Start()`. A missing init binary throws `ArgumentException 0x80070057` with the OCI message → surfaced as `WslContainerStartupException`. |
| `StopAsync` | Signal the init process with SIGTERM, allow 10 seconds to exit, then send SIGKILL and wait up to 5 seconds. Managed polling avoids the native blocking `Container.Stop` call and does not hold the session gate during the grace period. **Idempotent.** |
| `DeleteAsync` | `Container.Delete(Force)`. Double-delete throws `0x80010108` (`RPC_E_DISCONNECTED`) → swallowed. |
| `DisposeAsync` | Normally stop (if running), delete and release handles. Reusable containers and `WithCleanUp(false)` containers are stopped and retained. **Idempotent.** |

## Cleanup guarantees

- Use `await using` so caller failures always reach `DisposeAsync`. The runtime process-exit hook
  owns session cleanup; it is not a substitute for disposing each container.
- `EnableCleanUp` defaults to `true`, mirroring Testcontainers' `WithCleanUp`: `DisposeAsync` stops and
  removes the container. Docker also removes the container's anonymous volumes; named
  `WithVolumeMount` volumes are shared data and are retained (WSLC volumes belong to the session).
  Set `.WithCleanUp(false)` to stop and retain the container (and its volumes) for inspection, then
  remove it deliberately with `DeleteAsync` before disposal (or with the backend's own tooling).
  `WithReuse()` implies cleanup off; `WithReuse().WithCleanUp(true)` is rejected at build time.
- WSLC named volumes live on the session and are retained by default, matching Docker. Set
  `ContainerCleanup.CleanUpVolumesOnExit = true`, or `PURVIEW_CONTAINERS_CLEAN_VOLUMES=true` before the
  first container is created, to delete every volume the session recorded when it terminates. That is the
  session-wide, test-run switch: it removes the session's containers first and then those volumes, while
  leaving the shared image store intact. `WslContainerRuntimeOptions.CleanUpVolumesOnTerminate` overrides
  the shared switch (`null` inherits it), and `IContainerBackend.CleanUpAsync()` performs the cleanup
  explicitly.
- `EnableAutoRemove` defaults to `false`: the library owns deletion via `DisposeAsync`. WSLC auto-removal would delete one-shot containers before their state/output could be inspected. Opt in with `.WithAutoRemove()` when desired.
- Partial startup failure (image pull / create / start / wait): the container is torn down via the same `DisposeAsync` path.
- Cancellation: a cancelled `StartAsync`/wait triggers the same teardown.
- Builder-produced containers serialize start/stop/disposal. Disposal during startup cancels startup
  before waiting for cleanup, instead of waiting for the full startup budget.
- Container crash: live state/exit-code reads observe completion even when a native exit event is missed.
- Session termination (crash): the session `Terminated` event marks the runtime session as dead; subsequent container operations throw a clear `WslContainerSessionTerminatedException`.
- Test-process termination: a process-exit hook disposes the shared session (frees the name). A crashed process leaves an orphaned session; the runtime sweeps sessions whose creator PID is gone (and their per-session storage directories) before the next session starts, then removes stale containers whose generated name embeds a dead PID and prunes unused anonymous volumes. `WslContainerRuntime.SweepOrphanedSessionsAsync()` runs the session sweep explicitly; `CleanUpAsync()` also removes container residue.

## Secrets & names

- Container names are generated uniquely (`{service}-{processId}-{random8}`); never contain credentials.
- Session names are `purview-{pid}-{random8}`.
- `Secret.ToString()` returns `<redacted>`; CLI errors omit raw output. Container logs can still contain
  application secrets, so do not print them indiscriminately.

## Deadlines and reuse

Startup has a five-minute total managed budget (including image pull and all readiness checks).
Exec/exit waits default to one minute, stop to 15 seconds, cleanup/delete to 30 seconds, and file/log
snapshots to one minute. Fluent timeout overrides are available. Cancellation propagates as
`OperationCanceledException`, including WSLC stop and exec; it is not a successful exec result.
Partial startup cleanup may consume the separate cleanup budget before the original error returns.

Use `Wait.ForExitCode(expected)` for a one-shot init process, then `WaitForExitAsync(ct)` to obtain its
exit code. With reuse enabled, `DisposeAsync` retains the container and `DeleteAsync(ct)` explicitly
removes it. Docker reuse follows Testcontainers; WSLC reuse ends with its owning session.

Synchronous WSLC native calls cannot be safely interrupted. Managed waits and CLI commands are bounded,
not native SDK calls already entered. Do not treat cancellation as a hard operating-system guarantee.
See [Capabilities and Cancellation](Backend-Capabilities.md) for full ownership and limitation details.