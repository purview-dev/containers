# Purview.Containers.PostgreSql

Throwaway PostgreSQL databases for .NET integration testing on **WSL Containers (WSLC)** or **Docker**.

```bash
dotnet add package Purview.Containers.PostgreSql
```

Backend-neutral: depends on `Purview.Containers` and needs a backend package (`Purview.Containers.Wsl` or `Purview.Containers.Docker`) and brings `Npgsql` for connection-string generation.
See the [Getting Started guide](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Getting-Started.md).

## Requirements

- **WSL Containers backend:** Windows 10/11 with WSL Containers (`wsl --install --no-distribution`), and a
  consuming project that is a .NET 11 project targeting Windows specifically
  (`net11.0-windows10.0.19041.0`, x64 or arm64). The `Purview.Containers.Wsl` package is Windows-only and
  supplies `buildTransitive` defaults for `WindowsSdkPackageVersion`/`PlatformTarget`, rejecting an
  unsupported consumer with `PCC0001`/`PCC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Docker backend:** any reachable Docker daemon (`docker info`), with a `net10.0` or later project on any
  platform. No Windows target framework and no `PCC` guards apply.
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

## Quick start

```csharp
using Npgsql;
using Purview.Containers.PostgreSql;

await using var postgres = new PostgreSqlBuilder()
    .WithDatabase("tests")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

await postgres.StartAsync();   // waits for pg_isready

await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
await connection.OpenAsync();
```

## API

| Member | Purpose |
| -- | -- |
| `PostgreSqlBuilder()` / `PostgreSqlBuilder(string image)` | Default image `postgres:17`, or a custom image. |
| `PostgreSqlBuilder.PostgreSqlPort` (5432) | Container port, mapped to a random host port. |
| `WithDatabase(string)` | Sets `POSTGRES_DB` (default `postgres`). |
| `WithUsername(string)` | Sets `POSTGRES_USER` (default `postgres`). |
| `WithPassword(string)` | Sets `POSTGRES_PASSWORD` (default `postgres`); stored as a redacted `Secret`. |
| `PostgreSqlContainer.GetConnectionString()` | `NpgsqlConnectionStringBuilder` connection string for the mapped host port. |

`Build()` rejects an empty database, username or password with `ContainerConfigurationException`. The
default wait strategy runs `pg_isready -U {username} -d {database}` inside the container unless you supply
your own with `WithWaitStrategy(...)`. Call `GetConnectionString()` after `StartAsync()`.

## Documentation

- [Backends: WSLC or Docker](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
