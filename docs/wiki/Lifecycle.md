# Lifecycle

Container lifecycle semantics for `Purview.Containers`.

## Public API

```csharp
public interface IContainer : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    ValueTask DisposeAsync();
    // restart is intentionally NOT offered (WSLC has no restart; stop+start is not equivalent)
}
```

## State model

WSLC `ContainerState`: `Invalid → Created → Running → Exited → Deleted`.

| Operation | Behaviour |
|---|---|
| `StartAsync` | Creates the session if needed, ensures image (per pull policy), `CreateContainer`, then `Container.Start()`. A missing init binary throws `ArgumentException 0x80070057` with the OCI message → surfaced as `WslContainerStartupException`. |
| `StopAsync` | `Container.Stop(SIGTERM, grace)` then, if still running, `Stop(SIGKILL, ...)`. **Idempotent** — WSLC accepts stopping an already-stopped container. |
| `DeleteAsync` | `Container.Delete(Force)`. Double-delete throws `0x80010108` (`RPC_E_DISCONNECTED`) → swallowed. |
| `DisposeAsync` | Stop (if running) → Delete → release the WinRT container object. **Idempotent.** |

## Cleanup guarantees

- `DisposeAsync` runs in `finally` by callers, and the container object is also registered for cleanup by the runtime so disposal happens even if a test throws after `StartAsync`.
- `EnableAutoRemove` defaults to `false`: the library owns deletion via `DisposeAsync`. WSLC auto-removal would delete one-shot containers before their state/output could be inspected. Opt in with `.WithAutoRemove()` when desired.
- Partial startup failure (image pull / create / start / wait): the container is torn down via the same `DisposeAsync` path.
- Cancellation: a cancelled `StartAsync`/wait triggers the same teardown.
- Container crash: `InitProcess.Exited` fires; the container is then in `Exited` state and `DisposeAsync` deletes it.
- Session termination (crash): the session `Terminated` event marks the runtime session as dead; subsequent container operations throw a clear `WslContainerSessionTerminatedException`.
- Test-process termination: a process-exit hook disposes the shared session (frees the name). Crashed processes leave an orphaned session that only reserves its name; sweep with `wslc --session <name> system session terminate`.

## Secrets & names

- Container names are generated uniquely (`{service}-{processId}-{random8}`); never contain credentials.
- Session names are `wslc-{pid}-{random8}`.
- Diagnostics redact secrets via a `Secret` wrapper (`ToString()` returns `<redacted>`).