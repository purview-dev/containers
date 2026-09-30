# Purview.WslContainers.MySql

Throwaway MySQL databases for .NET integration testing, running as WSLC containers on
**Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.MySql
```

Depends on `Purview.WslContainers` (the core runtime) and brings `MySqlConnector` for readiness probing and
connection-string generation.

## Quick start

```csharp
using MySqlConnector;
using Purview.WslContainers.MySql;

await using var mysql = new MySqlBuilder()
    .WithDatabase("tests")
    .WithUsername("test")
    .WithPassword("test")
    .Build();

await mysql.StartAsync();   // waits for a real host-side connection

await using var connection = new MySqlConnection(mysql.GetConnectionString());
await connection.OpenAsync();
```

## API

| Member | Purpose |
| -- | -- |
| `MySqlBuilder()` / `MySqlBuilder(string image)` | Default image `mysql:8`, or a custom image. |
| `MySqlBuilder.MySqlPort` (3306) | Container port, mapped to a random host port. |
| `WithDatabase(string)` | Sets `MYSQL_DATABASE` (default `test`). |
| `WithUsername(string)` | Sets `MYSQL_USER` (default `test`). |
| `WithPassword(string)` | Sets `MYSQL_PASSWORD` (default `test`); stored as a redacted `Secret`. |
| `WithRootPassword(string)` | Sets `MYSQL_ROOT_PASSWORD` (default `test`); stored as a redacted `Secret`. |
| `MySqlContainer.GetConnectionString()` | `MySqlConnectionStringBuilder` connection string for the mapped host port. |

`Build()` rejects an empty database or password with `WslContainerConfigurationException`.

## Readiness

The image logs `ready for connections` while its temporary initialisation server is still running, so a log
match or a bare TCP check would report readiness too early. The default wait strategy therefore opens a real
host-side `MySqlConnector` connection (every second) and only returns once it succeeds. Supply your own
strategy with `WithWaitStrategy(...)` if you need different behaviour.

## Documentation

- [Modules](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
