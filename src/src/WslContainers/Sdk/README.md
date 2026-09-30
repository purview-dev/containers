# Purview.WslContainers

WSLC-native throwaway Linux containers for .NET integration testing — a Testcontainers-style library built
directly on the `Microsoft.WSL.Containers` managed API, with **no Docker installation** and no
`wslc.exe`/`wsl.exe`/`docker` CLI, Docker.DotNet or Testcontainers dependency.

```bash
dotnet add package Purview.WslContainers
```

## Requirements

- Windows 10/11 with **WSL ≥ 2.9.3** including WSL Containers (`wsl --install --no-distribution`).
- .NET 11 SDK or later (`net11.0-windows10.0.19041.0`).
- Verify with `wsl --version` (needs 2.9.3+) and `wslc version`. The library never installs or updates WSL
  itself; `WslContainerRuntime.GetInfoAsync()` reports what is missing.

## Quick start

```csharp
using Purview.WslContainers;
using Purview.WslContainers.Waiting;

await using var container = new ContainerBuilder()
    .WithImage("docker.io/library/redis:latest")
    .WithPortBinding(6379, assignRandomHostPort: true)
    .WithWaitStrategy(Wait.ForTcpPort(6379))
    .Build();

await container.StartAsync();

ushort port = container.GetMappedPublicPort(6379);
```

`Build()` snapshots the accumulated builder state into an immutable configuration and validates it, so
invalid images, ports and mounts fail during configuration rather than at pull time.

## Builder surface

| Member | Purpose |
| -- | -- |
| `WithImage(string)`, `WithImage(Image)`, `WithTag(string)` | Image reference; parsed and validated eagerly (`Image.Parse`). |
| `WithName`, `WithHostname`, `WithDomainName` | Container identity. Names are generated uniquely when unset. |
| `WithEnvironment(string, string)`, `WithEnvironment(IReadOnlyDictionary<string, string>)` | Init-process environment. |
| `WithCommand(params string[])`, `WithWorkingDirectory` | Init-process argv (no shell) and working directory. |
| `WithPortBinding(ushort, bool assignRandomHostPort)`, `WithPortBinding(ushort, ushort hostPort, ...)`, `WithPortBinding(PortBinding)` | Host port mappings; random host ports use native `windowsPort=0` allocation. |
| `WithBindMount(string hostPath, string containerPath, bool readOnly = false)` | Bind-mount a Windows directory. |
| `WithVolumeMount(string name, string containerPath, bool readOnly = false)` | Named volume on the session VHD. |
| `WithNetworkMode`, `WithPrivileged`, `WithGPU`, `WithAutoRemove`, `WithPullPolicy`, `WithStartupTimeout`, `WithWaitStrategy`, `WithRegistryCredentials`, `WithRuntime` | Runtime behaviour and readiness. |

## Container API

`IContainer` (and `WslContainer`) exposes `StartAsync`, `StopAsync`, `ExecAsync`, `GetMappedPublicPort`,
`GetMappedPublicPorts`, `GetLogsAsync(LogOutput?)` and a tailing `IAsyncEnumerable<ContainerLogEntry>`
`GetLogsAsync(CancellationToken)`. `Id`, `Name`, `State` and `Image` describe the running container.
`DisposeAsync` stops and deletes the container and is idempotent; it never terminates the shared session.

## Wait strategies

`Wait` composes readiness checks that run inside `StartAsync`:

| Factory | Ready when |
| -- | -- |
| `Wait.ForContainerRunning()` | The init process is running (started, not necessarily a ready service). |
| `Wait.ForTcpPort(port)` | A TCP connection to the mapped host port succeeds. |
| `Wait.ForHttp(path)` | An HTTP(S) request succeeds; configure `ForPort`, `ForStatusCode`, `ForHeader`, `AllowInsecureTls`. |
| `Wait.ForLogMessage(string)` / `Wait.ForLogMessage(Regex)` | Accumulated init-process output matches. |
| `Wait.ForCommand(params string[])` | A command executed in the container exits with the expected code. |
| `Wait.ForCustom(predicate)` | An arbitrary predicate returns `true`. |
| `Wait.ForAll(...)` / `Wait.ForAny(...)` | All / any contained strategy succeeds. |

Every strategy supports `.WithTimeout(...)`, `.WithInterval(...)` and `.WithRetries(...)`. The default
timeout is the container's `StartupTimeout` (5 minutes). Timeouts throw `WslContainerTimeoutException` with
a diagnostic naming the container, image, state, mapped ports, strategy and a bounded, secret-redacted log tail.

## Runtime, sessions and storage

`WslContainerRuntime.Instance` owns a single lazily started, process-wide WSLC session named
`wslc-{pid}-{random8}`. Images are **shared by default** (`StorageMode.Shared`): every session uses
`%LOCALAPPDATA%\Purview\WslContainers\images`, so images are pulled once and reused across process runs.
A session exclusively locks its `storage.vhdx`; when a concurrent process holds the default shared store the
runtime verifies the store once and transparently falls back to an isolated per-process store (removed when
that session terminates). Configure `WslContainerRuntimeOptions` for CPU, memory, GPU, session name,
`StoragePath` (or the `WSL_CONTAINERS_STORAGE_PATH` environment variable) and `StorageMode.PerSession`.

The Microsoft types (`Session`, `Container`, `Process`, …) stay behind the public interfaces; the only escape
hatch is the opt-in accessor for `Inspect()` and raw handles.

## Diagnostics

`System.Diagnostics.ActivitySource("Purview.WslContainers")` emits session, pull, container lifecycle, exec
and wait spans. Credentials and other sensitive values are wrapped in `Secret` and redacted from `ToString()`
and diagnostics.

## Documentation

See the [project wiki](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Home.md):
[Getting Started](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Getting-Started.md),
[Architecture](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Architecture.md),
[Lifecycle](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Lifecycle.md),
[Networking](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Networking.md) and
[Wait Strategies](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Wait-Strategies.md).
Ready-made service modules ship as `Purview.WslContainers.PostgreSql`, `Redis`, `MsSql`, `RabbitMq`,
`Azurite`, `Nats` and `MySql`.
