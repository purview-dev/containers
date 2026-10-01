# Purview.Containers

[![NuGet version](https://img.shields.io/nuget/v/Purview.Containers.Wsl.svg)](https://www.nuget.org/packages/Purview.Containers.Wsl)
[![Release](https://github.com/purview-dev/wsl-containers/actions/workflows/release.yml/badge.svg)](https://github.com/purview-dev/wsl-containers/actions/workflows/release.yml)

A Testcontainers-style library for .NET that runs throwaway Linux containers for integration testing — on **Microsoft WSL Containers (WSLC)** with no Docker installation, or on **Docker** through Testcontainers.

> **Two backends, one API.** Containers are created through the backend-neutral `Purview.Containers`
> abstractions, so the same test suite runs on **WSL Containers** (`Purview.Containers.Wsl`) or
> **Docker** (`Purview.Containers.Docker`, driven by Testcontainers). Selection is automatic by default
> and can be pinned with `PURVIEW_CONTAINERS_BACKEND`. A plain `net10.0` project — no Windows target
> framework needed — references `Purview.Containers.Wsl` and gets WSLC on a Windows developer machine
> and Docker on a Linux CI runner **without changing a line of test code or configuration**: the package
> is multi-target and its `net10.0` facade loads the WSLC implementation at run time on Windows and
> reports `wsl` as unavailable everywhere else.
>
> **[Backends: WSLC or Docker](docs/wiki/Backends.md)** — comparison, side-by-side project setup, CI
> example and troubleshooting.

The WSLC backend is built directly against the `Microsoft.WSL.Containers` NuGet package (the WSLC managed C# API) — no `wslc.exe`/`wsl.exe`/`docker` CLI. The Docker backend is built on [Testcontainers for .NET](https://dotnet.testcontainers.org/).

> **Experimental.** This project is an experiment in running throwaway containers through Microsoft
> WSL Containers. The public API, defaults and packaging rules can change between prereleases, and
> there is no production support guarantee. Pin the exact package version you build against, and read
> [Consumer Requirements](docs/wiki/Consumer-Requirements.md) before adopting it.

> **Status: preview.** Backend-neutral abstractions, two backends (WSL Containers and Docker), wait
> strategies, the `Image`/`Tag` parser, registry auth, observability, hardening, and **seven service
> modules** (PostgreSQL, Redis, SQL Server, RabbitMQ, Azurite, NATS, MySQL). On WSLC, **images are shared
> by default** (`StorageMode.Shared`): sessions reuse a stable image store
> (`%LOCALAPPDATA%\Purview\WslContainers\images`) so images are pulled once, not per session;
> `StorageMode.PerSession` provides isolation. Runnable samples live in `samples/getting-started`.

## Prerequisites

Pick a backend — see [Backends: WSLC or Docker](docs/wiki/Backends.md) for the comparison.

**WSL Containers backend**

- Windows 10/11 with **WSL Containers**, installed via `wsl --install --no-distribution` (verified against
  WSL 3.0.1.0).
- A consuming project that is **.NET 10 or later**. A platform-neutral `net10.0` project binds the
  portable facade and gets automatic WSLC-or-Docker selection; a Windows target framework
  (`net10.0-windows10.0.19041.0`, x64 or arm64) binds the implementation directly. The
  `Purview.Containers.Wsl` package ships MSBuild defaults for the supporting settings.

**Docker backend**

- Any reachable Docker daemon, and a `net10.0` (or later) project on any platform.

.NET SDK 11 is required to build this repository (it pins `11.0.100-rc.1`); SDK 10 is enough for a Docker
consumer.

Verify:

```powershell
wsl --version     # WSL Containers installed (verified against 3.0.1.0)
wslc version      # e.g. 3.0.1.0
```

```bash
docker info       # a Docker daemon the tests can reach
```

The library reports missing prerequisites via `WslContainerRuntime.GetInfoAsync()` (WSLC) or
`DockerContainerBackend.GetInfoAsync()` (Docker); it never installs a runtime on its own.

The full consumer contract, the exact errors raised when it is not met, and the `EnableWindowsTargeting`
workaround for non-Windows CI agents are documented in
[Consumer Requirements](docs/wiki/Consumer-Requirements.md).

## Target developer experience

The generic container API below is **implemented and working** — and the identical code runs on either
backend (see [Backends: WSLC or Docker](docs/wiki/Backends.md)):

```csharp
await using var container = new ContainerBuilder()
    .WithImage("docker.io/library/redis:latest")
    .WithPortBinding(6379, assignRandomHostPort: true)
    .WithWaitStrategy(Wait.ForTcpPort(6379))
    .Build();

await container.StartAsync();
int port = container.GetMappedPublicPort(6379);
```

PostgreSQL:

```csharp
await using var postgres = new PostgreSqlBuilder()
    .WithDatabase("integration")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

await postgres.StartAsync();

await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
await connection.OpenAsync();
```

SQL Server / Redis / RabbitMQ:

```csharp
await using var sqlServer = new MsSqlBuilder().WithPassword("SomeStrong!Password1").AcceptLicense().Build();
await sqlServer.StartAsync();
string sqlConn = sqlServer.GetConnectionString();

await using var redis = new RedisBuilder().Build();
await redis.StartAsync();
string redisConn = redis.GetConnectionString();

await using var rabbitMq = new RabbitMqBuilder().WithUsername("guest").WithPassword("guest").Build();
await rabbitMq.StartAsync();
string amqp = rabbitMq.GetConnectionString();
```

> These are the target APIs. The generic container is implemented; the module builders arrive in
> Phases 3–6. See the docs for the designed surface.

## Key design decisions (verified by spikes)

| Decision | Evidence |
| --- | --- |
| One shared process-wide session, shared storage path | image store is keyed by storage path; session start ~20 ms (S2); concurrent sessions can't share the VHD (S18) → auto-fallback to isolated store |
| Unique session names `wslc-{pid}-{rand}` | session names are machine-reserved (S1, S13) |
| Default `NetworkingMode = Bridged` | port mappings require Bridged; default is `none` (S4) |
| Random host ports via native `windowsPort=0` | race-free random assignment (S4) |
| Serialize session/container lifecycle ops | concurrent `Start` can race (`0x8000FFFF`) (S8) |
| No Ryuk-style reaper needed | `Session.Dispose()` frees the session; orphans block only their own name (S10, S14) |
| No container-name DNS | containers reach each other by IP only (S9) |
| `Image`/`WithTag` fail fast | invalid images/tags rejected at configuration time, never at pull time |

## Repository layout

```
src/
  Wsl/          core runtime + WSL Containers backend (package Purview.Containers.Wsl)
  PostgreSql/   PostgreSQL module (builder, container, Npgsql connection string)
  Redis/        Redis module (builder, container, StackExchange.Redis connection string)
  MsSql/        SQL Server module (builder, container, SqlClient connection string)
  RabbitMq/     RabbitMQ module (builder, container, AMQP + management endpoints)
  Azurite/      Azurite module (builder, container, blob/queue/table endpoints)
  Nats/         NATS module (builder, container, client + monitoring endpoints)
  MySql/        MySQL module (builder, container, MySqlConnector connection string)
tests/
  SharedTestingFramework/   shared WSLC skip/helper for integration tests
  Wsl.UnitTests/
  Wsl.IntegrationTests/
  PostgreSql.UnitTests/
  PostgreSql.IntegrationTests/
  Redis.UnitTests/
  Redis.IntegrationTests/
  MsSql.UnitTests/
  MsSql.IntegrationTests/
  RabbitMq.UnitTests/
  RabbitMq.IntegrationTests/
  Azurite.UnitTests/
  Azurite.IntegrationTests/
spikes/
  WslcSpikes/               Phase 0 investigation harness (run: see below)
docs/
  wiki/                     project wiki (mkdocs.yml -> docs_dir: docs/wiki)
    index.md  Home.md  _Sidebar.md  Getting-Started.md  Testing.md
    Architecture.md  Lifecycle.md  Networking.md  Wait-Strategies.md  Modules.md
    Packaging.md  Release-Flow.md  Contributing.md  Contributing-Modules.md
    Wslc-Api-Investigation.md  Wslc-Capability-Matrix.md
```

## Documentation

The project documentation lives in [`docs/wiki`](docs/wiki/Home.md) and is published as a MkDocs site
(`mkdocs.yml`, `docs_dir: docs/wiki`, aggregated by the purview-dev website):

- [Getting Started](docs/wiki/Getting-Started.md) — prerequisites, first container, first module.
- [Backends: WSLC or Docker](docs/wiki/Backends.md) — how to choose, side-by-side project setup, CI example, troubleshooting.
- [Consumer Requirements](docs/wiki/Consumer-Requirements.md) — the target-framework contract, the `PCC0001`/`PCC0002` guards, and the CI workarounds.
- [Architecture](docs/wiki/Architecture.md) — the shared session model, concurrency and cleanup decisions.
- [Lifecycle](docs/wiki/Lifecycle.md), [Networking](docs/wiki/Networking.md), [Wait Strategies](docs/wiki/Wait-Strategies.md).
- [Modules](docs/wiki/Modules.md) — the module contract and every shipped module.
- [Testing](docs/wiki/Testing.md) — unit vs integration categories and the serial WSLC test rule.
- [Packaging](docs/wiki/Packaging.md) and [Release Flow](docs/wiki/Release-Flow.md) — what ships and how it is released.
- [Contributing](docs/wiki/Contributing.md) and [Contributing Modules](docs/wiki/Contributing-Modules.md).

Every package also ships its own `README.md` (from `src/src/<Project>/Sdk/README.md`), so
`dotnet add package Purview.Containers.<Module>` brings documentation specific to that package.

The PostgreSQL, Redis, SQL Server and RabbitMQ modules work today:

```csharp
await using var postgres = new PostgreSqlBuilder()
    .WithDatabase("tests")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

await postgres.StartAsync();   // waits for pg_isready

await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
await connection.OpenAsync();
```

```csharp
await using var redis = new RedisBuilder().Build();

await redis.StartAsync();      // waits for redis-cli ping

using var redisConnection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
await redisConnection.GetDatabase().ExecuteAsync("PING");
```

```csharp
await using var sqlServer = new MsSqlBuilder()
    .WithPassword("SomeStrong!Password1")
    .AcceptLicense()           // explicit EULA acceptance required
    .Build();

await sqlServer.StartAsync();  // waits for a real SQL Server connection

await using var sqlConnection = new SqlConnection(sqlServer.GetConnectionString());
await sqlConnection.OpenAsync();
```

```csharp
await using var rabbitMq = new RabbitMqBuilder()
    .WithUsername("guest")
    .WithPassword("guest")
    .Build();

await rabbitMq.StartAsync();   // waits for 'Server startup complete'

var factory = new ConnectionFactory { Uri = rabbitMq.GetAmqpEndpoint() };
using var amqp = await factory.CreateConnectionAsync();
```

```csharp
await using var azurite = new AzuriteBuilder().Build();

await azurite.StartAsync();    // waits for the blob/queue/table listeners

// Supply the Azurite devstoreaccount1 key in AzuriteAccount.Key for authenticated operations.
string connectionString = azurite.GetConnectionString();
Uri blob = azurite.GetBlobEndpoint();
```

## Running the tests

Every test process owns a single shared WSLC session and, by default, uses the shared image store
(`%LOCALAPPDATA%\Purview\WslContainers\images`). A WSLC session **exclusively locks its
`storage.vhdx`**, and the lock is taken lazily on the first store access — so running test assemblies
in parallel makes the losers fail with `0x80070020`:

```
The process cannot access the file because it is being used by another process.
```

The runtime now verifies the store on first use and transparently falls back to an isolated
per-process store (removed when that process's session terminates), but running test modules serially
keeps the warm shared image cache (no per-process re-pull) and is the fastest option:

```powershell
just test                     # dotnet test, one test module at a time

# Visual Studio: Test > Options > untick "Run Tests in Parallel" (or set
# "Maximum Parallel Test Projects" to 1) before running the WSLC integration tests.
```

To watch a container start end to end, run a sample — the same code, on either backend:

```powershell
just sample-wsl               # needs WSL Containers
just sample-docker            # needs a Docker daemon
```

> The `Wsl.IntegrationTests` module runs 27 real containers in one session and takes ~4
> minutes because WSLC serialises container operations; expect slow-test warnings while it runs.

### Verifying the consumer contract

```powershell
just verify-consumers          # pack, then build 16 throwaway consumer projects against ./artifacts
just verify-consumers -Keep    # same, keeping the generated projects for inspection
```

`just verify-consumers` asserts every claim in [Consumer Requirements](docs/wiki/Consumer-Requirements.md):
the happy path, the shipped `buildTransitive` defaults, the `PCC0001`/`PCC0002` guards, and the
non-.NET-11 escape hatch that deliberately does not work. It needs network access and is therefore a
local step rather than part of the `[Category=Unit]` pipeline filter.

## Running the Phase 0 spikes

```powershell
dotnet build spikes/WslcSpikes/WslcSpikes.csproj
dotnet run --project spikes/WslcSpikes -- sfull
# or s1..s14 for individual behaviour probes
```

## License

MIT. This project is not affiliated with, or endorsed by, the Testcontainers project or Microsoft.
