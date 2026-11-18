# Wait strategies

Composable readiness waits. **A started WSLC container is not necessarily a ready service** — `Container.Start()` returns once the init process is running; readiness is checked separately.

## Model

Every strategy implements `IWaitStrategy`, and the built-in ones derive from `WaitStrategy`, which adds
the fluent `WithTimeout`, `WithInterval` and `WithRetries`:

```csharp
public interface IWaitStrategy
{
    TimeSpan? Timeout { get; }   // null: the container's StartupTimeout
    TimeSpan Interval { get; }   // default 250 ms
    int? Retries { get; }        // optional maximum number of failed checks

    Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken);
}

// WaitContext: Container, NetworkIp, GetHostPort(ushort containerPort), FirstHostPort
```

The `Wait` factory creates them:

| Factory | Ready when |
| --- | --- |
| `Wait.ForContainerRunning()` | The init process is running (not a service-readiness check) |
| `Wait.ForExitCode(long exitCode = 0)` | A one-shot container exits with the expected code |
| `Wait.ForTcpPort(ushort containerPort, TimeSpan? connectTimeout = null)` | A TCP connection to the mapped host port succeeds |
| `Wait.ForHttp(string path = "/")` | An HTTP(S) request succeeds; returns `HttpWaitStrategy` with `ForPort`, `ForScheme`, `ForStatusCode`, `ForStatusPredicate`, `ForHeader` and `AllowInsecureTls` |
| `Wait.ForLogMessage(string)`, `Wait.ForLogMessage(Regex)` | The container output contains the message |
| `Wait.ForCommand(params string[] command)` | A command run in the container succeeds. It returns `WaitStrategy`; the instance is a `CommandWaitStrategy`, whose `ForExitCode(int)` sets a different expected code |
| `Wait.ForCustom(Func<WaitContext, CancellationToken, Task<bool>>)` | Your predicate returns `true` |
| `Wait.ForAll(params IWaitStrategy[])`, `Wait.ForAny(params IWaitStrategy[])` | Every one, or any one, of the strategies is ready |

- Waits run inside `StartAsync()` after the container starts. `StartAsync` only returns once every configured strategy is ready (or throws `ContainerTimeoutException`).
- Default timeout = the container's `StartupTimeout` (5 min). This is also the total managed startup
  budget, including image/create/start and all strategies; per-strategy timeouts can shorten it, not
  extend it. `Wait.ForExitCode(expected)` supports one-shot completion.
- Timeout failure produces a diagnostic including container name, image, state, mapped ports, the strategy type, the last check error, and a bounded tail of stdout/stderr. Application output can contain secrets; diagnostics are not automatically safe to publish.
- An unexpected one-shot exit fails immediately with `ContainerExitCodeException`.

## Strategies

### Container running
`ForContainerRunning()` — checks `Container.State == Running`. This only means the init process started; it is NOT a service-readiness check.

### TCP
`ForTcpPort(8080)` — connects to `127.0.0.1:<mapped host port>`. Refused/reset/timeout ⇒ not ready. IPv4 only (WSLC maps IPv4 loopback only).

### HTTP
```csharp
Wait.ForHttp("/health")
    .ForPort(8080)
    .ForStatusCode(HttpStatusCode.OK)
```
- Scheme default `http`; `ForScheme("https")` supported.
- TLS certificate validation is **ON by default**; `AllowInsecureTls()` disables it for **this request handler only** (never global).

### Log
`ForLogMessage("database system is ready")` matches (case-insensitive substring) or regex against the **accumulated** init-process output. The container buffers output from process start, so messages emitted before the waiter's first check still match.

### Command/exec
`ForCommand("pg_isready", "-U", "postgres")` — executes the command via `ExecAsync`; ready when exit code equals the expected code (default 0, configurable via `.ForExitCode(...)`). Transient exec failures during startup are treated as not-ready.

### Custom / composition
`ForCustom((context, ct) => ...)` for arbitrary checks. `ForAll(...)` requires every strategy ready on the same check; `ForAny(...)` requires at least one.

## Started vs ready

- `ForContainerRunning()` = started.
- TCP/HTTP/log/command = service readiness.
- Modules choose their own readiness check (PostgreSQL `pg_isready`, Redis `redis-cli ping`, RabbitMQ `rabbitmq-diagnostics ping`, SQL Server host-side connection).