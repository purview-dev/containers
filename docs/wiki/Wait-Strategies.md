# Wait strategies

Composable readiness waits. **A started WSLC container is not necessarily a ready service** — `Container.Start()` returns once the init process is running; readiness is checked separately.

> **Status: implemented.** Verified by integration tests (`tests/Wsl.IntegrationTests/WaitStrategyTests.cs`).

## Model

```
IWaitStrategy
  TimeSpan? Timeout         // null → container StartupTimeout
  TimeSpan  Interval        // default 250 ms
  int?      Retries         // optional max failed checks
  Task<bool> UntilAsync(WaitContext context, CancellationToken ct)

WaitContext { IContainer Container; string? NetworkIp; int? GetHostPort(ushort); int? FirstHostPort; }

Wait (factory)
  ForContainerRunning()
  ForTcpPort(ushort containerPort, TimeSpan? connectTimeout = null)
  ForHttp(string path = "/") → HttpWaitStrategy { ForPort, ForScheme, ForStatusCode, ForStatusPredicate, ForHeader, AllowInsecureTls }
  ForLogMessage(string) | ForLogMessage(Regex)
  ForCommand(params string[]) → CommandWaitStrategy { ForExitCode }
  ForCustom(Func<WaitContext, CancellationToken, Task<bool>>)
  ForAll(params IWaitStrategy[]) | ForAny(params IWaitStrategy[])

WaitStrategy.WithTimeout / .WithInterval / .WithRetries   (fluent)
```

- Waits run inside `StartAsync()` after the container starts. `StartAsync` only returns once every configured strategy is ready (or throws `ContainerTimeoutException`).
- Default timeout = the container's `StartupTimeout` (5 min, overridable via `.WithStartupTimeout(...)` or per-strategy `.WithTimeout(...)`).
- Timeout failure produces a diagnostic including container name, image, state, mapped ports, the strategy type, the last check error, and a tail of stdout/stderr (bounded, secret-redacted).

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