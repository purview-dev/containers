# Using it in your tests (auto)

The zero-configuration shape: **one project, no backend choice** in your code or environment. The same
tests run on **WSL Containers** on a Windows developer machine and on **Docker** on a Linux CI runner (or
a Mac) — nothing changes between them.

## The project

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework>
	</PropertyGroup>
	<ItemGroup>
		<!-- the services you want -->
		<PackageReference Include="Purview.Containers.Redis" Version="1.0.0-prerelease.2" />
		<PackageReference Include="Purview.Containers.PostgreSql" Version="1.0.0-prerelease.2" />
		<!-- both backends: WSLC where it works, Docker everywhere else -->
		<PackageReference Include="Purview.Containers.Wsl" Version="1.0.0-prerelease.2" />
		<PackageReference Include="Purview.Containers.Docker" Version="1.0.0-prerelease.2" />
	</ItemGroup>
</Project>
```

Three things make this work, and nothing else is required:

- **A platform-neutral target framework** (`net10.0`). `Purview.Containers.Wsl` is multi-target; a
  `net10.0` project binds its portable facade, which runs the WSLC implementation on Windows and reports
  `wsl` unavailable everywhere else. (A Windows target framework still works — it binds the
  implementation directly — but it cannot run on a Linux CI runner.)
- **Both backend packages.** WSLC is preferred where it is usable and Docker is the fallback, so the one
  project runs everywhere. Referencing only the WSL backend leaves a Docker-only machine with nothing to
  fall back to.
- **No registration line, no environment variable.** Each backend package ships `buildTransitive` assets
  that generate a module initializer in your assembly, so the process discovers `wsl` and `docker` by
  itself.

## The test

```csharp
using Npgsql;
using Purview.Containers.PostgreSql;
using Purview.Containers.Redis;
using StackExchange.Redis;

public class CacheAndDatabaseTests
{
	[Test]
	public async Task PostgreSql_is_usable()
	{
		await using var postgres = new PostgreSqlBuilder().WithDatabase("app").Build();
		await postgres.StartAsync();                          // returns once pg_isready succeeds

		await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
		await connection.OpenAsync();
		// ...your assertions against a real database...
	}

	[Test]
	public async Task Redis_is_usable()
	{
		await using var redis = new RedisBuilder().Build();
		await redis.StartAsync();                             // returns once redis-cli ping succeeds

		await using var cache = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
		await cache.GetDatabase().PingAsync();
		// ...your assertions against a real cache...
	}
}
```

That is the whole story: no image names, no ports, no `Testcontainers`, no `docker`, no `wslc`. The
builder default image, the port mapping (a random host port), and the readiness check all come from the
module; `GetConnectionString()` points at the mapped host port.

## What happens on each host

| Host | Selected backend | Why |
| --- | --- | --- |
| Windows with WSL Containers | `wsl` | The facade loads the WSLC implementation and WSLC is preferred. |
| Windows without WSL Containers, with Docker | `docker` | The facade reports `wsl` unavailable, so selection falls through. |
| Linux / macOS (any CI runner) | `docker` | The facade only ever activates on Windows. |

Automatic selection probes the registered backends in priority order (`wsl` is `0`, `docker` is `100`) and
returns the first one that is both **available** and **compatible**. When none is usable, the exception
lists each backend's availability, version and missing components — see
[Backends: WSLC or Docker](Backends.md).

## Pinning, and seeing what it picked

Auto is the default and needs no configuration. Two things help while debugging:

```csharp
// Which backends can this machine run, and what version?
foreach (var backend in await ContainerBackends.ProbeAllAsync())
{
    Console.WriteLine($"{backend.Name}: usable={backend.IsUsable} version={backend.Version}");
}
```

```bash
# Fail loudly instead of falling back (the usual choice in CI).
PURVIEW_CONTAINERS_BACKEND=docker   # or: wsl
```

A named backend never silently falls back: if it cannot run, the test fails with that backend's own
diagnostics.

## Requirements

- The abstractions and every service module are portable `net10.0`; see
  [Consumer Requirements](Consumer-Requirements.md) for the exact target-framework contract, the
  `PCC0001`/`PCC0002` guards and the `EnableWindowsTargeting` workaround for non-Windows build agents.
- The per-module connection-string shapes (Redis, PostgreSQL, SQL Server, MySQL, RabbitMQ, Azurite, NATS)
  are in [Modules](Modules.md#connection-strings).
- A live sample of this shape is `samples/getting-started/AutoSample` (`just sample-auto`).

## Related

- [Getting Started](Getting-Started.md) — install and the first container.
- [Backends: WSLC or Docker](Backends.md) — the comparison, side-by-side setup and CI examples.
- [Modules](Modules.md) — the service modules and their connection strings.
- [Consumer Requirements](Consumer-Requirements.md) — the target-framework contract and the guards.