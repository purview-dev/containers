# Getting Started

This guide installs the packages, runs a first generic container, switches to a typed service module, and
points you at the test workflow.

## Requirements

- Windows 10/11 with **WSL ≥ 2.9.3** including WSL Containers (`wsl --install --no-distribution`).
- .NET SDK 11 or later; the packages target `net11.0-windows10.0.19041.0` (x64).
- Verify the host with `wsl --version` (needs 2.9.3+) and `wslc version`. The library never installs or
  updates WSL for you — call `WslContainerRuntime.GetInfoAsync()` to report what is missing.

## 1. Reference a package

Reference the core runtime directly for a generic container:

```bash
dotnet add package Purview.WslContainers
```

or a service module, which depends on the core package:

```bash
dotnet add package Purview.WslContainers.PostgreSql
```

## 2. Run a generic container

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

`Build()` validates the accumulated configuration; `StartAsync()` ensures the session and image, creates the
container, starts it, and only returns once every configured wait strategy is satisfied. `DisposeAsync()`
stops and deletes the container (and never terminates the shared session).

## 3. Use a typed module

```csharp
using Npgsql;
using Purview.WslContainers.PostgreSql;

await using var postgres = new PostgreSqlBuilder()
    .WithDatabase("tests")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

await postgres.StartAsync();   // waits for pg_isready

await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
await connection.OpenAsync();
```

Each module ships a bespoke README inside the package (`Purview.WslContainers.<Module>`) and a page in the
[Modules](Modules.md) reference.

## 4. Run the tests

```powershell
just test                 # dotnet test, one test module at a time
just test '/*/*/*/*[Category=Unit]'   # unit tests only (no WSLC required)
```

Integration tests need a working WSLC installation. A WSLC session exclusively locks its image-store VHD, so
test modules run serially by default — see [Testing](Testing.md) for the details and how to run a subset.

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
