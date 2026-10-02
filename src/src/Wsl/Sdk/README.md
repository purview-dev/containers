# Purview.Containers.Wsl

The **WSL Containers (WSLC) backend** for [`Purview.Containers.Core`](https://www.nuget.org/packages/Purview.Containers.Core):
throwaway Linux containers for .NET integration testing built directly on the `Microsoft.WSL.Containers`
managed API, with **no Docker installation** and no `wslc.exe`/`wsl.exe`/`docker` CLI, Docker.DotNet or
Testcontainers dependency.

```bash
dotnet add package Purview.Containers.Wsl
```

Reference this package (or a service module) and containers run on WSLC. The package is
**multi-target**: a `net10.0` build (a portable facade) and a `net10.0-windows10.0.19041.0` build (the
implementation). A Windows-targeting project binds the implementation; a `net10.0` project binds the
facade, which loads the implementation at run time on a Windows host and reports `wsl` as unavailable
elsewhere. It registers itself as the `wsl` backend in the consuming assembly. To also run the same test
code on Docker in Linux CI, reference
[`Purview.Containers.Docker`](https://www.nuget.org/packages/Purview.Containers.Docker) (or the umbrella
[`Purview.Containers`](https://www.nuget.org/packages/Purview.Containers), which brings both backends).

> **Running in CI, or on a machine without WSL Containers?** With only this package referenced, `wsl` is
> reported unavailable and there is **no** backend to fall back to. Add `Purview.Containers.Docker` or use
> the umbrella `Purview.Containers`, and `auto` (the default) falls through to Docker. Pin instead with
> `PURVIEW_CONTAINERS_BACKEND=wsl|docker` or `ContainerBackends.Use(...)` — see
> [Backends: WSLC or Docker](https://github.com/purview-dev/containers/blob/main/docs/wiki/Backends.md).

## Requirements

- Windows 10/11 with **WSL Containers** (`wsl --install --no-distribution`), verified against WSL 3.0.1.0.
- **A .NET 10 project.** The recommended target framework is platform-neutral (`net10.0`): it binds the
  facade and gives you automatic WSLC-or-Docker selection. A Windows target framework
  (`net10.0-windows10.0.19041.0`, x64 or arm64) binds the implementation directly. Anything older than
  .NET 10, or a Windows TFM below Windows 10.0.19041.0, fails the build with `PCC0001`; a 32-bit
  Windows consumer fails with `PCC0002`; and without the packages' MSBuild defaults a stale
  `WindowsSdkPackageVersion` fails with `CS1705`.
- This package supplies the `buildTransitive` defaults for `WindowsSdkPackageVersion` and
  `PlatformTarget` that every module package inherits, and (for a platform-neutral consumer on a Windows
  build host) copies the Windows implementation payload next to the output. The full contract, the error
  reference and the `EnableWindowsTargeting` workaround for non-Windows CI agents are in the
  [consumer requirements](https://github.com/purview-dev/containers/blob/main/docs/wiki/Consumer-Requirements.md).
- Verify the host with `wsl --version` and `wslc version`. The library never installs or updates WSL
  itself; `WslContainerRuntime.GetInfoAsync()` reports what is missing.
- **Experimental:** this is an experiment in driving WSL Containers. The public API, defaults and
  packaging rules can change between prereleases, and there is no production support guarantee.

## Quick start

```csharp
using Purview.Containers.Wsl;
using Purview.Containers.Waiting;

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
timeout is the container's `StartupTimeout` (5 minutes). Timeouts throw `ContainerTimeoutException` with
a diagnostic naming the container, image, state, mapped ports, strategy and a bounded, secret-redacted log tail.

## Runtime, sessions and storage

`WslContainerRuntime.Instance` owns a single lazily started, process-wide WSLC session named
`wslc-{pid}-{random8}`. Images are **shared by default** (`StorageMode.Shared`): every session uses
`%LOCALAPPDATA%\Purview\WslContainers\images`, so images are pulled once and reused across process runs.
A session exclusively locks its `storage.vhdx`; when a concurrent process holds the default shared store the
runtime verifies the store once and transparently falls back to an isolated per-process store (removed when
that session terminates). Configure `WslContainerRuntimeOptions` for CPU, memory, GPU, session name,
`StoragePath` (or the `PURVIEW_CONTAINERS_STORAGE_PATH` environment variable) and `StorageMode.PerSession`.

The Microsoft types (`Session`, `Container`, `Process`, …) stay behind the public interfaces; the only escape
hatch is the opt-in accessor for `Inspect()` and raw handles.

## Diagnostics

`System.Diagnostics.ActivitySource("Purview.Containers")` emits session, pull, container lifecycle, exec
and wait spans. Credentials and other sensitive values are wrapped in `Secret` and redacted from `ToString()`
and diagnostics.

## Documentation

See the [project wiki](https://github.com/purview-dev/containers/blob/main/docs/wiki/Home.md):
[Getting Started](https://github.com/purview-dev/containers/blob/main/docs/wiki/Getting-Started.md),
[Architecture](https://github.com/purview-dev/containers/blob/main/docs/wiki/Architecture.md),
[Lifecycle](https://github.com/purview-dev/containers/blob/main/docs/wiki/Lifecycle.md),
[Networking](https://github.com/purview-dev/containers/blob/main/docs/wiki/Networking.md) and
[Wait Strategies](https://github.com/purview-dev/containers/blob/main/docs/wiki/Wait-Strategies.md).
Ready-made service modules ship as `Purview.Containers.PostgreSql`, `Redis`, `MsSql`, `RabbitMq`,
`Azurite`, `Nats` and `MySql`.
