# Purview.WslContainers Wiki

Purview.WslContainers is a **WSLC-native** Testcontainers-style library for .NET: it runs throwaway Linux
containers for integration testing on **Microsoft WSL Containers (WSLC)** with **no Docker installation**.
It is built directly against the `Microsoft.WSL.Containers` managed package — no `wslc.exe`/`wsl.exe`/
`docker` CLI, no Docker.DotNet, and no Testcontainers dependency.

This wiki is the project documentation hub. The packages are published under the
`Purview.WslContainers.*` package IDs.

## Start here

- [Getting Started](Getting-Started.md)
- [Architecture](Architecture.md)
- [Lifecycle](Lifecycle.md)
- [Networking](Networking.md)
- [Wait Strategies](Wait-Strategies.md)
- [Modules](Modules.md)

## Packages

| Package | Purpose |
| --- | --- |
| `Purview.WslContainers` | Core runtime: `ContainerBuilder`, sessions, images, ports, mounts, wait strategies, logs/exec, diagnostics. |
| `Purview.WslContainers.PostgreSql` | PostgreSQL container (`postgres:17`), `pg_isready` readiness, Npgsql connection string. |
| `Purview.WslContainers.Redis` | Redis-compatible container (`redis:7`), `redis-cli ping` readiness; also usable with Valkey/Garnet. |
| `Purview.WslContainers.MsSql` | SQL Server container (`mssql/server:2022-latest`), host-side `SqlClient` readiness, explicit EULA acceptance. |
| `Purview.WslContainers.RabbitMq` | RabbitMQ container (`rabbitmq:3-management`), AMQP + management endpoints. |
| `Purview.WslContainers.Azurite` | Azure Storage emulator (`azure-storage/azurite`), blob/queue/table endpoints. |
| `Purview.WslContainers.Nats` | NATS broker (`nats:2`), client + monitoring endpoints. |
| `Purview.WslContainers.MySql` | MySQL container (`mysql:8`), host-side `MySqlConnector` readiness. |

## Feature highlights

- **One shared, process-wide session** — a stable storage path gives a warm image cache
  (`StorageMode.Shared`), and the runtime transparently falls back to an isolated per-process store when a
  concurrent process holds the shared store VHD.
- **Race-free random host ports** — native `windowsPort=0` allocation, read back from the container's mapped
  ports, instead of probing for a free port first.
- **Fail-fast configuration** — invalid images and tags are rejected at configuration time (`Image.Parse`),
  UDP mappings and port `0` are rejected at build time, and module invariants (licences, passwords, required
  fields) throw `WslContainerConfigurationException` before a pull is attempted.
- **Composable readiness** — TCP, HTTP(S), log-message, exec and custom wait strategies, with timeouts,
  intervals, retries, and `ForAll`/`ForAny` composition.
- **Module model without duplication** — modules supply defaults (image, ports, environment, readiness,
  connection strings) on top of the core runtime; they never re-implement container infrastructure.
- **Observability and secrecy** — an `ActivitySource` spans session, pull, lifecycle, exec and wait;
  credentials live in a `Secret` wrapper and never reach logs or `ToString()` output.

## Requirements

- Windows 10/11.
- **WSL ≥ 2.9.3** with WSL Containers, installed via `wsl --install --no-distribution`.
- .NET SDK 11 (the repository pins `11.0.100-rc.1.26425.128`).

```powershell
wsl --version     # needs 2.9.3+
wslc version      # prints e.g. 3.0.1.0
```

The library reports missing prerequisites through `WslContainerRuntime.GetInfoAsync()`; it never installs or
updates WSL on its own.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/WSLTestContainers.slnx` | Canonical solution for restore, build, test and pack. |
| `src/src/WslContainers` | Core runtime (`Purview.WslContainers`): containers, images, runtime, networking, mounts, diagnostics. |
| `src/src/<Module>` | Service modules; each carries a bespoke `Sdk/README.md` that ships as the package README. |
| `src/tests` | TUnit unit and integration test projects. |
| `spikes/WslcSpikes` | Phase 0 investigation harness (`s1`..`s14` behaviour probes). |
| `docs/wiki` | This wiki, aggregated by the purview-dev website. |
| `purview-build.json` | Shared `Purview.Build` pipeline configuration, including the exhaustive pack manifest. |
| `.agents` | Package-delivered agent skills, agents and prompts. |

## Workflow

- [Testing](Testing.md) — unit vs integration categories, session locking, and the serial test rule.
- [Packaging](Packaging.md) — what each package ships and how the pack manifest is validated.
- [Release Flow](Release-Flow.md) — versioning, pipeline commands and the release gate.
- [Contributing](Contributing.md) — build, lint, test and pack locally.
- [Contributing Modules](Contributing-Modules.md) — adding a new service module.
