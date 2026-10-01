# Backends: WSL Containers or Docker

`Purview.Containers` has **one API and two interchangeable backends**. The same test code, the same service
modules (`Purview.Containers.PostgreSql`, `Redis`, `MsSql`, `RabbitMq`, `Azurite`, `Nats`, `MySql`), and
the same connection-string accessors run on either runtime — you choose which one, in code or from the
environment.

- `**Purview.Containers.Wsl**` — throwaway containers on **Microsoft WSL Containers (WSLC)**, the Windows
  runtime with no Docker installation.
- `**Purview.Containers.Docker**` — throwaway containers on **any reachable Docker daemon**, driven by
  Testcontainers.

## At a glance

|  | WSL Containers | Docker |
| --- | --- | --- |
| **Package** | `Purview.Containers.Wsl` | `Purview.Containers.Docker` |
| **Prerequisite** | Windows 10/11 with WSL Containers (`wsl --install --no-distribution`) | any reachable Docker daemon (Docker Desktop, Docker Engine in WSL2, a VM, or a CI runner) |
| **Host OS** | Windows only | Windows, Linux, macOS |
| **Project target framework** | `net10.0` or later, any platform (portable facade), or `net10.0-windows10.0.19041.0`, x64 or arm64 (implementation bound directly) | `net10.0` or later, any platform |
| **How containers run** | the `Microsoft.WSL.Containers` managed API (daemonless) | the Docker Engine API via Testcontainers |
| **Images** | a shared store (`%LOCALAPPDATA%\Purview\WslContainers\images`) reused across runs | the daemon's own image store |
| **Leak protection** | session disposal plus a process-exit hook | the Testcontainers resource reaper (Ryuk) |
| **Check the host** | `wsl --version`, `wslc version` | `docker info` |
| **Typical fit** | local Windows development without Docker Desktop, fastest cold start | CI runners, non-Windows hosts, teams already running Docker |

Switch between them without touching test code:

```bash
# auto (the default) | wsl | docker | <any registered backend name>
export PURVIEW_CONTAINERS_BACKEND=docker      # bash / zsh / CI
```

```powershell
$env:PURVIEW_CONTAINERS_BACKEND = 'wsl'       # PowerShell
```

## Verify the host before you rely on it

Each backend reports its own readiness, so a missing runtime is a clear message instead of a timeout deep
inside a test.

WSL Containers:

```powershell
wsl --version      # WSL itself
wslc version       # the WSLC runtime
```

```csharp
using Purview.Containers.Wsl;

var info = await new WslContainerBackend().GetInfoAsync();
Console.WriteLine($"{info.Name}: usable={info.IsUsable} version={info.Version}");

if (!info.IsUsable)
{
    Console.WriteLine(string.Join("; ", info.MissingComponents));
}
```

Docker:

```bash
docker info
```

```csharp
using Purview.Containers.Docker;

var info = await new DockerContainerBackend().GetInfoAsync();
Console.WriteLine($"{info.Name}: usable={info.IsUsable} version={info.Version}");

if (!info.IsUsable)
{
    Console.WriteLine(string.Join("; ", info.MissingComponents));
}
```

`ContainerBackends.ProbeAllAsync()` reports every registered backend at once, which is the quickest way to
answer "what can this machine run?":

```csharp
foreach (var backend in await ContainerBackends.ProbeAllAsync())
{
    Console.WriteLine($"{backend.Name}: usable={backend.IsUsable} version={backend.Version}");
}
```

## The same test, either backend

The test body never names a backend, so it is identical on both runtimes:

```csharp
using Purview.Containers;
using Purview.Containers.Waiting;

public class CacheTests
{
    [Test]
    public async Task Cache_IsReachableOnItsMappedPort()
    {
        await using var container = new ContainerBuilder()
            .WithImage("redis:7")
            .WithPortBinding(6379, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForTcpPort(6379))
            .Build();

        await container.StartAsync();

        ushort port = container.GetMappedPublicPort(6379);

        await Assert.That(port).IsGreaterThan((ushort)0);
    }
}
```

The **package reference** decides which runtime executes it. Three project shapes cover every case.

### Option 1 — WSLC on a Windows machine, Docker elsewhere (one project)

```bash
dotnet add package Purview.Containers.Wsl
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework><!-- auto: WSLC on Windows, Docker elsewhere -->
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Purview.Containers.Wsl" Version="1.0.0-prerelease.1" />
	</ItemGroup>
</Project>
```

A platform-neutral `net10.0` project binds the portable facade, so it selects WSLC on a Windows host
and Docker everywhere else. A Windows target framework (`net10.0-windows10.0.19041.0`, x64 or arm64)
binds the implementation directly. `WindowsSdkPackageVersion` is supplied by the package. A project
older than .NET 10 fails the build with `PCC0001`, and a 32-bit Windows consumer with `PCC0002`; see
[Consumer Requirements](Consumer-Requirements.md).

### Option 2 — Docker anywhere

```bash
dotnet add package Purview.Containers.Docker
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Purview.Containers.Docker" Version="1.0.0-prerelease.1" />
	</ItemGroup>
</Project>
```

That project restores and builds on Linux, macOS and Windows — no Windows target framework, no
`WindowsSdkPackageVersion`, no Docker Desktop licence requirement beyond the daemon you already run.

### Option 3 — one project, both backends (auto)

Reference both backends from a single **platform-neutral** project. `auto` selects WSLC on a machine that
can run it and Docker otherwise — no multi-targeting and no conditional references:

```bash
dotnet add package Purview.Containers.Wsl
dotnet add package Purview.Containers.Docker
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework><!-- WSLC on Windows, Docker elsewhere -->
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Purview.Containers.Wsl" Version="1.0.0-prerelease.1" />
		<PackageReference Include="Purview.Containers.Docker" Version="1.0.0-prerelease.1" />
	</ItemGroup>
</Project>
```

`auto` probes both in priority order (`wsl` before `docker`) and uses the first that is usable, so this
one project runs on WSLC on a developer's Windows machine and on Docker in a Linux CI job. A Windows
target framework is still supported when you want the implementation bound at compile time, but it is not
required.

If your code needs a Windows-only API the portable facade does not expose (for example `WslContainer`, or
`WslContainerBackend(runtime)` to pin a specific WSLC session), guard it with `#if WINDOWS` — defined by
the SDK only for a Windows target framework — so a portable target still compiles:

```csharp
#if WINDOWS
using Purview.Containers.Wsl;
#endif

// ...
#if WINDOWS
var backend = new WslContainerBackend(runtimeForIsolation);
#else
var backend = new DockerContainerBackend();
#endif
```

### Typed modules work the same way

A module is backend-neutral, so only the backend package line changes:

```bash
dotnet add package Purview.Containers.PostgreSql   # the module
dotnet add package Purview.Containers.Wsl          # ...or Purview.Containers.Docker
```

```csharp
using Npgsql;
using Purview.Containers.PostgreSql;

await using var postgres = new PostgreSqlBuilder()
    .WithDatabase("tests")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

await postgres.StartAsync();   // ready when pg_isready succeeds, on either backend

await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
await connection.OpenAsync();
```

## Choosing at run time

Selection is resolved once per process, in this order:

| # | Rule | How to set it | Behaviour |
| --- | --- | --- | --- |
| 1 | Pinned instance | `ContainerBackends.Use(new DockerContainerBackend())`, or `WithBackend(...)` on one builder | used as-is: never probed, never substituted |
| 2 | Named backend | `PURVIEW_CONTAINERS_BACKEND=wsl\|docker\|<name>`, or `ContainerBackends.Use(ContainerBackendSelection.Named("docker"))` | probed; a missing or unusable backend fails with its own diagnostics and **no fallback** |
| 3 | Automatic detection | the default (`auto`) | every registered backend is probed in auto-priority order (`wsl` at 0, `docker` at 100); the first *available and compatible* one wins, so WSLC is preferred over Docker |

```csharp
using Purview.Containers;

// Automatic detection (the default)...
var container = new ContainerBuilder().WithImage("alpine:3.19").Build();

// ...or pin it for this builder only.
var pinned = new ContainerBuilder()
    .WithBackend(new DockerContainerBackend())
    .WithImage("alpine:3.19")
    .Build();

// ...or pin it for the process.
ContainerBackends.Use(ContainerBackendSelection.Named("docker"));
```

`Build()` never needs a backend: it validates the configuration and returns a container that resolves the
backend when it starts. That is what lets the same test suite run on whichever runtime the machine has.

To fail loudly instead of falling back — the usual choice in CI — name the backend:

```
PURVIEW_CONTAINERS_BACKEND=docker   # if Docker is not reachable, the test fails and says why
```

## In CI

A hosted Linux runner already has a Docker daemon, so nothing has to be started alongside your tests —
reference `Purview.Containers.Docker` in a `net10.0` test project and run `dotnet test`:

```yaml
name: tests

on: [push, pull_request]

jobs:
  integration-linux:
    name: Integration tests (Docker)
    runs-on: ubuntu-latest
    env:
      # Optional. "auto" (the default) also selects Docker when WSL Containers is absent.
      PURVIEW_CONTAINERS_BACKEND: docker
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore
      - run: dotnet test --configuration Release --no-build
```

- On a Linux job, target a platform-neutral framework (`net10.0` or later). The WSL Containers package is
  portable — its `net10.0` facade loads the WSLC implementation on a Windows host and reports `wsl` as
  unavailable elsewhere — so one test project can reference both backends and `auto` selects WSLC on a
  Windows developer machine and Docker on the Linux runner.
- Keep your integration tests in their own project or category if you want the fast unit tests to stay
  runtime-free; both can run in the same job.
- Pinning `PURVIEW_CONTAINERS_BACKEND=docker` makes a runner without a usable daemon **fail loudly** with
  the probe report instead of quietly selecting something else.

For a **Windows** job that should exercise WSLC, run the same code against `Purview.Containers.Wsl`:

```yaml
  integration-windows:
    name: Integration tests (WSL Containers)
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 11.0.x
      - run: dotnet test --configuration Release
```

> WSL Containers is a Windows developer runtime: hosted GitHub runners do not guarantee a WSL Containers
> installation, so the practical options are a **self-hosted Windows runner**, or running WSLC suites
> locally and Docker suites in CI. Either way the test code is the same.

## What differs in practice

| Capability | WSL Containers | Docker |
| --- | --- | --- |
| Random host ports | ✅ native (`windowsPort=0`) | ✅ assigned by the daemon |
| Fixed host ports | ✅ | ✅ |
| IPv6 host mapping | ❌ IPv4 loopback only | ✅ |
| UDP port mappings | ❌ `ContainerNotSupportedException` | ✅ |
| Bind mounts of host directories | ✅ | ✅ |
| Named volumes | ✅ (session VHD) | ✅ (Docker volumes) |
| GPU exposure | ✅ (`EnableGPU` session setting) | depends on the daemon and host runtime |
| `ExecOptions.WorkingDirectory` / `Timeout` | ✅ | ✅ |
| `ExecOptions.Environment` | ✅ | ❌ `ContainerNotSupportedException` |
| Log tailing | streamed while the init process runs | polled until the container stops |
| Leak protection | session disposal + process-exit hook | Testcontainers resource reaper (Ryuk) |
| Image store | shared WSLC store, warm across runs | the daemon's store |
| Lifetime cost | one process-wide session, cheap start | first pull per image, per daemon |

Unsupported options throw `ContainerNotSupportedException` rather than being ignored, so a difference
between backends never turns into a silently weaker test.

## Troubleshooting

**`No container backend is registered.`** — no backend package is referenced by the test project (and the
assembly that would register it was never loaded). Add one:
`dotnet add package Purview.Containers.Docker` or `dotnet add package Purview.Containers.Wsl`.

**`No usable container backend was found (selection: auto).`** — every registered backend failed its probe;
the report names each one and why:

```text
No usable container backend was found (selection: auto).
  wsl: unavailable (Sdk; SdkNeedsUpdate)
  docker: unavailable (HttpRequestException: Connection refused)
Install or fix a backend, or set PURVIEW_CONTAINERS_BACKEND to one of: wsl, docker.
```

Fix the runtime it names (for WSLC: `wsl --install --no-distribution`; for Docker: start the daemon, or
point `DOCKER_HOST` at it), or pin the backend that does work.

**`The selected container backend 'docker' is not usable: …`** — you named a backend that cannot run here.
This is intentional: a named backend never falls back to another one, so a CI job cannot pass by silently
using the wrong runtime.

**`The container backend 'docker' is not registered. Registered backends: wsl.`** — the selection names a
backend whose package is not referenced by the project.

**`PCC0001` / `PCC0002`** — a build-time guard from the WSL Containers backend package: the consuming project
is neither .NET 10+ (Windows or platform-neutral), or is a 32-bit Windows consumer. See
[Consumer Requirements](Consumer-Requirements.md), or switch the project to the Docker backend.

**The same image is pulled twice** — expected when both runtimes are used: WSLC and Docker keep separate
image stores.

## Related

- [Getting Started](Getting-Started.md) — install, first container, first typed module.
- [Consumer Requirements](Consumer-Requirements.md) — the target-framework contract, the guards and the workarounds.
- [Architecture](Architecture.md#backend-selection) — how resolution and registration work internally.
- [Modules](Modules.md) — the service modules and their readiness strategies.



