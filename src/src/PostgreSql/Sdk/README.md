# Purview.WslContainers.PostgreSql

Throwaway PostgreSQL databases for .NET integration testing, running as WSLC containers on
**Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.PostgreSql
```

Depends on `Purview.WslContainers` (the core runtime) and brings `Npgsql` for connection-string generation.
See the [Getting Started guide](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Getting-Started.md).

## Requirements

- Windows 10/11 with **WSL Containers** (`wsl --install --no-distribution`).
- **A .NET 11 project targeting Windows specifically** (`net11.0-windows10.0.19041.0`, x64 or arm64).
  `Purview.WslContainers` supplies `buildTransitive` defaults for `WindowsSdkPackageVersion` and
  `PlatformTarget`, and rejects an unsupported consumer with `PWC0001`/`PWC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

## Quick start

```csharp
using Npgsql;
using Purview.WslContainers.PostgreSql;

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

`Build()` rejects an empty database, username or password with `WslContainerConfigurationException`. The
default wait strategy runs `pg_isready -U {username} -d {database}` inside the container unless you supply
your own with `WithWaitStrategy(...)`. Call `GetConnectionString()` after `StartAsync()`.

## Documentation

- [Modules](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
