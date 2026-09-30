# Purview.WslContainers

A **WSLC-native** Testcontainers-style library for .NET that runs throwaway Linux containers for integration testing on **Microsoft WSL Containers (WSLC)** — with no Docker installation.

Built directly against the `Microsoft.WSL.Containers` NuGet package (the WSLC managed C# API). No `wslc.exe`/`wsl.exe`/`docker` CLI, no Docker.DotNet, no Testcontainers internally.

> **Status: Phase 8 in progress.** Core runtime, wait strategies, the `Image`/`Tag` parser, registry auth,
> observability, hardening, and **eight service modules** (PostgreSQL, Redis, SQL Server, RabbitMQ, Azurite,
> NATS, MySQL, MinIO). **Images are shared by default** (`StorageMode.Shared`): sessions reuse a stable
> image store (`%LOCALAPPDATA%\Purview\WslContainers\images`) so images are pulled once, not per session;
> `StorageMode.PerSession` provides isolation. 69+ unit tests pass across all modules.

## Prerequisites

- Windows 10/11
- **WSL ≥ 2.9.3** with WSL Containers, installed via `wsl --install --no-distribution`
- .NET SDK 11 (the repo pins `11.0.100-rc.1`)

Verify:

```powershell
wsl --version     # needs 2.9.3+
wslc version      # prints e.g. 3.0.1.0
```

The library reports missing prerequisites via `WslContainerRuntime.GetInfoAsync()`; it never installs or updates WSL on its own.

## Target developer experience

The generic container API below is **implemented and working**:

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
|---|---|
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
  WslContainers/            core runtime (Containers, Images, Runtime, Networking, Mounts, Diagnostics)
  WslContainers.PostgreSql/ PostgreSQL module (builder, container, Npgsql connection string)
  WslContainers.Redis/      Redis module (builder, container, StackExchange.Redis connection string)
  WslContainers.MsSql/      SQL Server module (builder, container, SqlClient connection string)
  WslContainers.RabbitMq/   RabbitMQ module (builder, container, AMQP + management endpoints)
  WslContainers.Azurite/    Azurite module (builder, container, blob/queue/table endpoints)
  WslContainers.Nats/       NATS module (builder, container, client + monitoring endpoints)
  WslContainers.MySql/      MySQL module (builder, container, MySqlConnector connection string)
  WslContainers.MinIo/      MinIO module (builder, container, S3 API + console endpoints)
tests/
  SharedTestingFramework/   shared WSLC skip/helper for integration tests
  WslContainers.UnitTests/
  WslContainers.IntegrationTests/
  WslContainers.PostgreSql.UnitTests/
  WslContainers.PostgreSql.IntegrationTests/
  WslContainers.Redis.UnitTests/
  WslContainers.Redis.IntegrationTests/
  WslContainers.MsSql.UnitTests/
  WslContainers.MsSql.IntegrationTests/
  WslContainers.RabbitMq.UnitTests/
  WslContainers.RabbitMq.IntegrationTests/
  WslContainers.Azurite.UnitTests/
  WslContainers.Azurite.IntegrationTests/
spikes/
  WslcSpikes/               Phase 0 investigation harness (run: see below)
docs/
  architecture.md  wslc-api-investigation.md  wslc-capability-matrix.md
  lifecycle.md  networking.md  wait-strategies.md  modules.md
  contributing-modules.md
```

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

> The `WslContainers.IntegrationTests` module runs 27 real containers in one session and takes ~4
> minutes because WSLC serialises container operations; expect slow-test warnings while it runs.

## Running the Phase 0 spikes

```powershell
dotnet build spikes/WslcSpikes/WslcSpikes.csproj
dotnet run --project spikes/WslcSpikes -- sfull
# or s1..s14 for individual behaviour probes
```

## License

MIT. This project is not affiliated with, or endorsed by, the Testcontainers project or Microsoft.