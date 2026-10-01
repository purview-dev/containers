# Purview.Containers.MsSql

Throwaway Microsoft SQL Server databases for .NET integration testing on **WSL Containers (WSLC)** or **Docker**.

```bash
dotnet add package Purview.Containers.MsSql
```

Backend-neutral: depends on `Purview.Containers` and needs a backend package (`Purview.Containers.Wsl` or `Purview.Containers.Docker`) and brings `Microsoft.Data.SqlClient` for
connection-string generation and readiness probing.

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
using Microsoft.Data.SqlClient;
using Purview.Containers.MsSql;

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

## Behaviour and constraints

- **Memory:** SQL Server refuses to start below 2000 MB. The default session VM is capped at 4096 MB
  (`WslContainerRuntimeOptions.Default`), so it works out of the box; override with
  `MemorySizeInMB` if you configure your own runtime.
- **Licence:** `Build()` throws `ContainerConfigurationException` unless `AcceptLicense()` was called.
  The SA password must be at least 8 characters.
- **Readiness:** a listening TCP port is not enough — the image reports readiness before it can serve
  queries, so the default strategy opens a host-side `SqlClient` connection every 2 seconds. The
  connection string targets `127.0.0.1`, because WSLC maps IPv4 loopback only and `Microsoft.Data.SqlClient`
  hangs on the IPv6 `::1` address that `localhost` can resolve to.

## Documentation

- [Backends: WSLC or Docker](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Getting Started](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Getting-Started.md) — prerequisites and first-container walkthrough.
