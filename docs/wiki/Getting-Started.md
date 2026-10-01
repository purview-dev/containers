# Getting Started

This guide installs the packages, runs a first generic container, switches to a typed service module, and
points you at the test workflow.

## Requirements

Everything runs on one of two backends; pick the one that matches your machine — see
[Backends: WSLC or Docker](Backends.md) for the full comparison.

- **WSL Containers backend:** Windows 10/11 with **WSL Containers** (`wsl --install --no-distribution`),
  verified against WSL **3.0.1.0**. A consuming project must be **.NET 10 or later**: a platform-neutral
  `net10.0` project binds the portable facade and gets automatic WSLC-or-Docker selection, while a Windows
  target framework (`net10.0-windows10.0.19041.0`, x64 or arm64) binds the implementation directly. The
  `Purview.Containers.Wsl` package ships MSBuild defaults for `WindowsSdkPackageVersion` and
  `PlatformTarget`; an unsupported target framework fails the build with `PCC0001` and a 32-bit Windows
  consumer with `PCC0002`.
- **Docker backend:** any reachable Docker daemon, on any platform, with a `net10.0` or later project. No
  Windows target framework and no `PCC` guards apply.
- Verify the host with `wsl --version` and `wslc version`, or with `docker info`. The library never
  installs a runtime for you — `WslContainerRuntime.GetInfoAsync()` and
  `DockerContainerBackend.GetInfoAsync()` report what is missing.
- The full consumer contract, every error, and the `EnableWindowsTargeting` workaround for non-Windows CI
  agents live in [Consumer Requirements](Consumer-Requirements.md).

> **Experimental.** The API, defaults and packaging rules can change between prereleases; there is no
> production support guarantee.

## 1. Reference a package

Reference a backend package for generic containers:

```bash
dotnet add package Purview.Containers.Wsl      # WSL Containers (WSLC on Windows, Docker elsewhere)
dotnet add package Purview.Containers.Docker   # Docker / Testcontainers (any platform)
```

or a service module, which is backend-neutral and needs a backend package alongside it:

```bash
dotnet add package Purview.Containers.PostgreSql
dotnet add package Purview.Containers.Wsl      # ...or Purview.Containers.Docker
```

## 2. Run a generic container

The same code works on either backend — only the package reference from step 1 decides where it runs:

```csharp
using Purview.Containers;
using Purview.Containers.Waiting;

await using var container = new ContainerBuilder()
    .WithImage("docker.io/library/redis:latest")
    .WithPortBinding(6379, assignRandomHostPort: true)
    .WithWaitStrategy(Wait.ForTcpPort(6379))
    .Build();

await container.StartAsync();

ushort port = container.GetMappedPublicPort(6379);
```

`Build()` validates the accumulated configuration and resolves the backend when the container starts;
`StartAsync()` creates the container, starts it, and only returns once every configured wait strategy is
satisfied. `DisposeAsync()` stops and deletes the container (and never terminates a shared WSLC session).

## 3. Use a typed module

```csharp
using Npgsql;
using Purview.Containers.PostgreSql;

await using var postgres = new PostgreSqlBuilder()
    .WithDatabase("tests")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

await postgres.StartAsync();   // waits for pg_isready

await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
await connection.OpenAsync();
```

Each module ships a bespoke README inside the package (`Purview.Containers.<Module>`) and a page in the
[Modules](Modules.md) reference.

## 4. Run the tests

```powershell
just test                 # dotnet test, one test module at a time
just test '/*/*/*/*[Category=Unit]'   # unit tests only (no runtime required)
```

Integration tests need a **running backend**: WSL Containers or a Docker daemon, depending on which you
selected. A WSLC session exclusively locks its image-store VHD, so test modules run serially by default —
see [Testing](Testing.md) for the details and how to run a subset.

To see a container start end to end, run one of the samples in this repository:

```powershell
just sample-wsl      # needs WSL Containers
just sample-docker   # needs a Docker daemon
```

## 5. Build and pack locally

```powershell
just build                # dotnet build of src/WSLTestContainers.slnx (Debug)
just pack                 # build + dotnet pack into ./artifacts
just pipeline-pack-validate   # shared pipeline: restore, build, lint, test, pack, validate
```

See [Contributing](Contributing.md) for the full local workflow and [Release Flow](Release-Flow.md) for
versioning and publishing.

## Next steps

- [Architecture](Architecture.md) — the session model, concurrency rules and cleanup decisions.
- [Wait Strategies](Wait-Strategies.md) — started vs ready, and composing your own checks.
- [Networking](Networking.md) — port mapping behaviour under WSLC.
- [Packaging](Packaging.md) — what lands in each package.
