# Purview.WslContainers.MsSql

Throwaway Microsoft SQL Server databases for .NET integration testing, running as WSLC containers on
**Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.MsSql
```

Depends on `Purview.WslContainers` (the core runtime) and brings `Microsoft.Data.SqlClient` for
connection-string generation and readiness probing.

## Quick start

```csharp
using Microsoft.Data.SqlClient;
using Purview.WslContainers.MsSql;

await using var sqlServer = new MsSqlBuilder()
    .WithPassword("SomeStrong!Password1")
    .AcceptLicense()   // explicit EULA acceptance is required
    .Build();

await sqlServer.StartAsync();   // waits for a real SQL Server connection

await using var connection = new SqlConnection(sqlServer.GetConnectionString());
await connection.OpenAsync();
```

## API

| Member | Purpose |
| -- | -- |
| `MsSqlBuilder()` / `MsSqlBuilder(string image)` | Default image `mcr.microsoft.com/mssql/server:2022-latest`, or a custom image. |
| `MsSqlBuilder.MsSqlPort` (1433) | Container port, mapped to a random host port. |
| `WithPassword(string)` | Sets `MSSQL_SA_PASSWORD` (default `YourStrong!Passw0rd`); stored as a redacted `Secret`. |
| `AcceptLicense()` | Sets `ACCEPT_EULA=Y`. Required — the library never accepts licensing terms on your behalf. |
| `MsSqlContainer.GetConnectionString()` | `SqlConnectionStringBuilder` connection string for the mapped host port. |

## Requirements and behaviour

- **Memory:** SQL Server refuses to start below 2000 MB. The default session VM is capped at 4096 MB
  (`WslContainerRuntimeOptions.Default`), so it works out of the box; override with
  `MemorySizeInMB` if you configure your own runtime.
- **Licence:** `Build()` throws `WslContainerConfigurationException` unless `AcceptLicense()` was called.
  The SA password must be at least 8 characters.
- **Readiness:** a listening TCP port is not enough — the image reports readiness before it can serve
  queries, so the default strategy opens a host-side `SqlClient` connection every 2 seconds. The
  connection string targets `127.0.0.1`, because WSLC maps IPv4 loopback only and `Microsoft.Data.SqlClient`
  hangs on the IPv6 `::1` address that `localhost` can resolve to.

## Documentation

- [Modules](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Getting Started](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Getting-Started.md) — prerequisites and first-container walkthrough.
