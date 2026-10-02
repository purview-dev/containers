# Connection strings

Every container implements `IConnectionStringProvider`, so you can ask any container for its connection
string without knowing the module type:

```csharp
IContainer container = new PostgreSqlBuilder().Build();
await container.StartAsync();

string connectionString = container.GetConnectionString();            // ConnectionMode.Host
string same = container.GetConnectionString(ConnectionMode.Host);     // explicit
```

## Connection modes

`ConnectionMode` describes how the connection string targets the container:

| Mode | Meaning | Support |
| --- | --- | --- |
| `Host` | Test host → container, using the mapped host port (`127.0.0.1:{port}`). | ✅ both backends |
| `Container` | Container → container, using the container network. | ❌ not supported yet |

`ConnectionMode.Container` throws `ConnectionStringModeNotSupportedException`. Neither backend supports
container-to-container networking today: WSLC has no managed networks or inter-container DNS, and the
Docker backend does not attach containers to a shared network. Multi-container wiring is deliberately
deferred (see [Networking](Networking.md)).

## How it works

A module's builder registers a connection string provider that delegates to the module's own
`GetConnectionString()`:

```csharp
sealed class PostgreSqlConnectionStringProvider
    : ContainerConnectionStringProvider<PostgreSqlContainer, PostgreSqlConfiguration>
{
    protected override string GetHostConnectionString() => Container.GetConnectionString();
}
```

`ContainerConnectionStringProvider<TContainer, TConfiguration>` is the base class: `Configure` runs once,
after the container has started (so runtime-assigned ports are available), and dispatches
`GetConnectionString(ConnectionMode)` to `GetHostConnectionString()` /
`GetContainerConnectionString()`. The named overload (`GetConnectionString(name, mode)`) exists for modules
with several endpoints; the base throws `ConnectionStringNameNotSupportedException` unless a provider
overrides it.

## Custom provider

Override the connection string a container exposes with `WithConnectionStringProvider`:

```csharp
sealed class ReadOnlyPostgreSql : ContainerConnectionStringProvider<PostgreSqlContainer, PostgreSqlConfiguration>
{
    protected override string GetHostConnectionString() => $"{Container.GetConnectionString()};ApplicationName=readonly";
}

var postgres = new PostgreSqlBuilder()
    .WithConnectionStringProvider(new ReadOnlyPostgreSql())
    .Build();
```

Providers that produce an empty connection string throw `ConnectionStringNotAvailableException`, and a
provider used before `Configure` throws `ConnectionStringProviderNotConfiguredException`.

## Module reference

See [Modules](Modules.md) for each module's default connection string and any extra endpoint accessors.
