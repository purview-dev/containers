# Modules

Module architecture for `Purview.Containers`.

## Principle

Modules are thin packages layered on the backend-neutral abstractions
([`Purview.Containers.Core`](Architecture.md)). A module supplies only:

- default image
- default ports
- default environment variables
- module-specific configuration (`WithXxx`)
- module-specific wait strategy
- connection string / endpoint generation
- module-specific convenience APIs

Modules must **not** duplicate container runtime infrastructure, and they must never reference a backend
package (`Purview.Containers.Wsl`, …): the container base resolves the backend through
`ContainerBackends.ResolveAsync()`, which is what lets the same module package run on WSLC or Docker. Every
module targets `net10.0` and is portable.

## Builder model

A generic CRTP base with immutable built configurations:

```csharp
public abstract class ContainerBuilder<TBuilder, TContainer, TConfiguration>
    where TBuilder : ContainerBuilder<TBuilder, TContainer, TConfiguration>
    where TContainer : IContainer
    where TConfiguration : ContainerConfiguration, new()
{
    public TBuilder WithImage(string image) { /* accumulate */ return (TBuilder)this; }
    // ... every fluent method returns (TBuilder)this

    public virtual TContainer Build()
    {
        TConfiguration configuration = BuildConfiguration(); // immutable snapshot
        Validate(configuration);
        return CreateContainer(configuration);
    }

    protected virtual TConfiguration BuildConfiguration();       // override to add module fields
    protected virtual void Validate(ContainerConfiguration);     // override to add module validation
    protected abstract TContainer CreateContainer(TConfiguration configuration);
}
```

- The builder accumulates mutable working state; `Build()` snapshots it into an **immutable `ContainerConfiguration` record**. No mutable state leaks between builders.
- Modules override `BuildConfiguration()` to set module fields via `with` on the base snapshot, and override `CreateContainer(...)` to construct their container type.
- Configuration `ToString()` redacts sensitive environment values and `Secret`-typed fields (see `Diagnostics/SecretRedactor.cs`).

## Reference module (PostgreSQL)

```csharp
public sealed class PostgreSqlBuilder : ContainerBuilder<PostgreSqlBuilder, PostgreSqlContainer, PostgreSqlConfiguration>
{
    public const ushort PostgreSqlPort = 5432;
    public const string PostgreSqlImage = "postgres:17";

    public PostgreSqlBuilder()
        : this(PostgreSqlImage)
    {
    }

    public PostgreSqlBuilder(string image)
    {
        WithImage(image).WithPortBinding(PostgreSqlPort, assignRandomHostPort: true);
    }

    public PostgreSqlBuilder WithDatabase(string database)
    {
        this.database = database;
        WithEnvironment("POSTGRES_DB", database);
        return this;
    }

    protected override PostgreSqlConfiguration BuildConfiguration()
    {
        PostgreSqlConfiguration configuration = base.BuildConfiguration();
        return configuration with
        {
            Database = database,
            Username = username,
            Password = password,
            // default readiness unless the caller supplied their own strategies
            WaitStrategies = configuration.WaitStrategies.Count > 0
                ? configuration.WaitStrategies
                : new[] { (IWaitStrategy)Wait.ForCommand("pg_isready", "-U", username, "-d", database) },
        };
    }

    protected override PostgreSqlContainer CreateContainer(PostgreSqlConfiguration configuration)
        => new(configuration, Backend);
}

public sealed class PostgreSqlContainer : ContainerBase
{
    private readonly PostgreSqlConfiguration configuration;

    internal PostgreSqlContainer(PostgreSqlConfiguration configuration, IContainerBackend backend)
        : base(configuration, backend) => this.configuration = configuration;

    public string GetConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort),
            Database = configuration.Database,
            Username = configuration.Username,
            Password = configuration.Password.Value,
        };
        return builder.ConnectionString;
    }
}
```

## Connection strings

- Never cached before port mapping is final; generated from runtime state (`GetMappedPublicPort`).
- Prefer client connection-string builders: `NpgsqlConnectionStringBuilder`, `SqlConnectionStringBuilder`, `UriBuilder`, etc. Avoid handcrafted escaping.
- Credentials are stored as `Secret` in the module configuration; diagnostics and `ToString()` never reveal them.

Every module exposes `GetConnectionString()` with the same shape it has in Testcontainers, so test code
that leans on the Testcontainers modules ports across unchanged:

| Module | `GetConnectionString()` | Extra accessors |
|---|---|---|
| PostgreSQL | Npgsql string: `Host`, `Port`, `Database`, `Username`, `Password` | — |
| Redis | `host:port` (e.g. `localhost:6379`) | — |
| SQL Server | `SqlConnectionStringBuilder`: `Data Source=host,port`, `Database` (default `master`, set with `WithDatabase`), `User Id=sa`, `Password`, `TrustServerCertificate=True` | — |
| MySQL | `MySqlConnectionStringBuilder`: `Server`, `Port`, `Database`, `User ID`, `Password` | — |
| RabbitMQ | `amqp://user:pass@host:port/vhost` | `GetAmqpEndpoint()`, `GetManagementEndpoint()` |
| Azurite | Azure Storage string: `DefaultEndpointsProtocol=http`, `AccountName`, `AccountKey`, `Blob/Queue/TableEndpoint` | `GetBlobEndpoint()`, `GetQueueEndpoint()`, `GetTableEndpoint()` |
| NATS | `nats://host:port` | `GetClientEndpoint()`, `GetMonitoringEndpoint()` |

## Module status

| Module | Image | Readiness | Client | Status |
|---|---|---|---|---|
| PostgreSQL | `postgres:17` | `pg_isready` | Npgsql | ✅ implemented |
| Redis | `redis:7` | `redis-cli ping` | StackExchange.Redis | ✅ implemented — also usable with Valkey/Garnet via `WithImage` |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-latest` | host `SqlClient` connection | Microsoft.Data.SqlClient | ✅ implemented — requires `.AcceptLicense()`; the connection string defaults to `Database=master` (`WithDatabase(...)` to change it); session needs ≥ 2000 MB memory |
| RabbitMQ | `rabbitmq:3-management` | log `"Server startup complete"` | RabbitMQ.Client | ✅ implemented — AMQP + management endpoints |
| Azurite | `mcr.microsoft.com/azure-storage/azurite` | log `"successfully listening"` | Azure.Storage.* | ✅ implemented — blob/queue/table endpoints; the well-known `devstoreaccount1` key is a placeholder in `AzuriteAccount.Key` until the consuming repo supplies it |
| NATS | `nats:2` | log `"Listening for client connections"` | NATS.Client.Core | ✅ implemented — client + monitoring endpoints |
| MySQL | `mysql:8` | host `MySqlConnector` connection | MySqlConnector | ✅ implemented — uses a real connection poll (the image logs `"ready for connections"` during its temporary init server) |

Note on RabbitMQ readiness: the default wait uses the canonical **`Server startup complete`** log signal rather than `rabbitmq-diagnostics ping` — under WSLC the exec-based probe races with the Erlang cookie setup and can trigger a startup failure (`eacces` reading `.erlang.cookie`). WSLC auto-provisions image `VOLUME` declarations on an ext4 device by default; bind-mounting a Windows directory onto one replaces it with drvfs, which ignores Unix `chown` and breaks permission-sensitive images — do not bind-mount onto image volumes unless you intend that.

## Custom modules (third-party)

See [Contributing Modules](Contributing-Modules.md).