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
| `DisposeAsync` | Normally stop (if running), delete and release handles. Reusable containers are retained. **Idempotent.** |

## Cleanup guarantees

- Use `await using` so caller failures always reach `DisposeAsync`. The runtime process-exit hook
  owns session cleanup; it is not a substitute for disposing each container.
- `EnableAutoRemove` defaults to `false`: the library owns deletion via `DisposeAsync`. WSLC auto-removal would delete one-shot containers before their state/output could be inspected. Opt in with `.WithAutoRemove()` when desired.
- Partial startup failure (image pull / create / start / wait): the container is torn down via the same `DisposeAsync` path.
- Cancellation: a cancelled `StartAsync`/wait triggers the same teardown.
- Builder-produced containers serialize start/stop/disposal. Disposal during startup cancels startup
  before waiting for cleanup, instead of waiting for the full startup budget.
- Container crash: live state/exit-code reads observe completion even when a native exit event is missed.
- Session termination (crash): the session `Terminated` event marks the runtime session as dead; subsequent container operations throw a clear `WslContainerSessionTerminatedException`.
- Test-process termination: a process-exit hook disposes the shared session (frees the name). Crashed processes leave an orphaned session that only reserves its name; sweep with `wslc --session <name> system session terminate`.

## Secrets & names

- Container names are generated uniquely (`{service}-{processId}-{random8}`); never contain credentials.
- Session names are `wslc-{pid}-{random8}`.
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