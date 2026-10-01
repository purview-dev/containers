# Purview.Containers.MySql

Throwaway MySQL databases for .NET integration testing on **WSL Containers (WSLC)** or **Docker**.

```bash
dotnet add package Purview.Containers.MySql
```

Backend-neutral: depends on `Purview.Containers` and needs a backend package (`Purview.Containers.Wsl` or `Purview.Containers.Docker`) and brings `MySqlConnector` for readiness probing and
connection-string generation.

## Requirements

- **WSL Containers backend:** Windows 10/11 with WSL Containers (`wsl --install --no-distribution`), and a
  `.NET 10` or later project. A platform-neutral `net10.0` project binds the portable facade and gets
  automatic WSLC-or-Docker selection; a Windows target framework
  (`net10.0-windows10.0.19041.0`, x64 or arm64) binds the implementation directly. The
  `Purview.Containers.Wsl` package supplies `buildTransitive` defaults for
  `WindowsSdkPackageVersion`/`PlatformTarget` and (for a platform-neutral consumer on a Windows build
  host) the implementation payload, rejecting an unsupported consumer with `PCC0001`/`PCC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Docker backend:** any reachable Docker daemon (`docker info`), with a `net10.0` or later project on any
  platform. No Windows target framework and no `PCC` guards apply.
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

## Quick start

```csharp
using MySqlConnector;
using Purview.Containers.MySql;

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

`Build()` rejects an empty database or password with `ContainerConfigurationException`.

## Readiness

The image logs `ready for connections` while its temporary initialisation server is still running, so a log
match or a bare TCP check would report readiness too early. The default wait strategy therefore opens a real
host-side `MySqlConnector` connection (every second) and only returns once it succeeds. Supply your own
strategy with `WithWaitStrategy(...)` if you need different behaviour.

## Documentation

- [Backends: WSLC or Docker](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
