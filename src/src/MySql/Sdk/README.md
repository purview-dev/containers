# Purview.WslContainers.MySql

Throwaway MySQL databases for .NET integration testing, running as WSLC containers on
**Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.MySql
```

Depends on `Purview.WslContainers` (the core runtime) and brings `MySqlConnector` for readiness probing and
connection-string generation.

## Requirements

- Windows 10/11 with **WSL Containers** (`wsl --install --no-distribution`).
- **A .NET 11 project targeting Windows specifically** (`net11.0-windows10.0.19041.0`, x64 or arm64).
  `Purview.WslContainers` supplies `buildTransitive` defaults for `WindowsSdkPackageVersion` and
  `PlatformTarget`, and rejects an unsupported consumer with `PWC0001`/`PWC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

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

- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
