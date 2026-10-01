# Purview.Containers Wiki

Purview.Containers is a Testcontainers-style library for .NET that runs throwaway Linux containers for
integration testing on **Microsoft WSL Containers (WSLC)** with **no Docker installation**, or on
**Docker** through Testcontainers. The WSLC backend is built directly against the
`Microsoft.WSL.Containers` managed package — no `wslc.exe`/`wsl.exe`/`docker` CLI.

This wiki is the project documentation hub. The packages are published under the
`Purview.Containers.*` package IDs.

> **Experimental.** This project is an experiment in running throwaway containers through Microsoft
> WSL Containers. The public API, defaults and packaging rules can change between prereleases, and
> there is no production support guarantee. Pin the exact package version you build against and read
> [Consumer Requirements](Consumer-Requirements.md) before adopting it.

## Start here

- [Getting Started](Getting-Started.md)
- [Backends: WSLC or Docker](Backends.md)
- [Consumer Requirements](Consumer-Requirements.md)
- [Architecture](Architecture.md)
- [Lifecycle](Lifecycle.md)
- [Networking](Networking.md)
- [Wait Strategies](Wait-Strategies.md)
- [Modules](Modules.md)

## Packages

| Package | Purpose |
| --- | --- |
| `Purview.Containers` | The umbrella: references `Purview.Containers.Core` and both backends, so one reference runs the same tests on WSLC on Windows and Docker elsewhere. No code of its own. |
| `Purview.Containers.Core` | Backend-neutral abstractions: the container contract, builders, wait strategies, images, networking, mounts, diagnostics, backend selection (namespace `Purview.Containers`). |
| `Purview.Containers.Wsl` | The WSL Containers backend: sessions, images, ports, mounts, wait strategies, logs/exec, diagnostics for WSLC. |
| `Purview.Containers.Docker` | The Docker backend: the same containers on any reachable Docker daemon, driven by Testcontainers. |
| `Purview.Containers.PostgreSql` | PostgreSQL container (`postgres:17`), `pg_isready` readiness, Npgsql connection string. |
| `Purview.Containers.Redis` | Redis-compatible container (`redis:7`), `redis-cli ping` readiness; also usable with Valkey/Garnet. |
| `Purview.Containers.MsSql` | SQL Server container (`mssql/server:2022-latest`), host-side `SqlClient` readiness, explicit EULA acceptance. |
| `Purview.Containers.RabbitMq` | RabbitMQ container (`rabbitmq:3-management`), AMQP + management endpoints. |
| `Purview.Containers.Azurite` | Azure Storage emulator (`azure-storage/azurite`), blob/queue/table endpoints. |
| `Purview.Containers.Nats` | NATS broker (`nats:2`), client + monitoring endpoints. |
| `Purview.Containers.MySql` | MySQL container (`mysql:8`), host-side `MySqlConnector` readiness. |

## Feature highlights

- **One shared, process-wide session** — a stable storage path gives a warm image cache
  (`StorageMode.Shared`), and the runtime transparently falls back to an isolated per-process store when a
  concurrent process holds the shared store VHD.
- **Race-free random host ports** — native `windowsPort=0` allocation, read back from the container's mapped
  ports, instead of probing for a free port first.
- **Fail-fast configuration** — invalid images and tags are rejected at configuration time (`Image.Parse`),
  UDP mappings and port `0` are rejected at build time, and module invariants (licences, passwords, required
  fields) throw `ContainerConfigurationException` before a pull is attempted.
- **Composable readiness** — TCP, HTTP(S), log-message, exec and custom wait strategies, with timeouts,
  intervals, retries, and `ForAll`/`ForAny` composition.
- **Module model without duplication** — modules supply defaults (image, ports, environment, readiness,
  connection strings) on top of the core runtime; they never re-implement container infrastructure.
- **Observability and secrecy** — an `ActivitySource` spans session, pull, lifecycle, exec and wait;
  credentials live in a `Secret` wrapper and never reach logs or `ToString()` output.

## Requirements

- **WSL Containers backend:** Windows 10/11 with **WSL Containers**, installed via
  `wsl --install --no-distribution`. Verified against WSL **3.0.1.0**; `wsl --version` and `wslc version`
  should both report 3.0.1.0 or later.
- **Docker backend:** any reachable Docker daemon (Docker Desktop, Docker Engine in WSL2, or a CI runner).
  Verify with `docker info`.
- .NET SDK 11 to build (the repository pins `11.0.100-rc.1.26425.128` in `global.json`).
- A consuming project must be a **.NET 10 or later** project when it uses the **WSL Containers backend**.
  The recommended shape is platform-neutral (`net10.0`): it binds the portable facade and gets automatic
  WSLC-or-Docker selection. A Windows target framework (`net10.0-windows10.0.19041.0`, x64 or arm64) binds
  the implementation directly. The abstractions and the service modules target `net10.0` and are portable,
  but they need a backend package to run — see
  [Backends: WSLC or Docker](Backends.md) for the side-by-side setup. The full contract, the exact errors
  raised when it is not met, and the `EnableWindowsTargeting` workaround for non-Windows CI agents are in
  [Consumer Requirements](Consumer-Requirements.md).

```powershell
wsl --version     # WSL Containers installed (verified against 3.0.1.0)
wslc version      # e.g. 3.0.1.0
```

The library reports missing prerequisites through `WslContainerRuntime.GetInfoAsync()`; it never installs or
updates WSL on its own.

## Choosing a backend

Two backends implement the same API. Automatic detection is the default; pin one in code or from the
environment:

```powershell
# auto (the default) | wsl | docker | <registered backend name>
$env:PURVIEW_CONTAINERS_BACKEND = "wsl"
```

```csharp
ContainerBackends.Use(ContainerBackendSelection.Named("docker"));   // or Use(new WslContainerBackend())
```

That is the switch that lets one test suite use WSLC on a developer machine and Docker in CI. Automatic
detection prefers WSLC and falls through to Docker when WSL Containers is not installed; a **named**
backend never silently falls back.

**[Backends: WSLC or Docker](Backends.md)** has the full comparison, side-by-side project setup, the CI
example and the troubleshooting reference.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/WSLTestContainers.slnx` | Canonical solution for restore, build, test and pack. |
| `src/src/Containers` | Umbrella package (`Purview.Containers`): references Core and both backends; no code of its own. |
| `src/src/Core` | Backend-neutral abstractions (`Purview.Containers.Core`, namespace `Purview.Containers`): the container contract, builders and backend selection. |
| `src/src/Wsl` | WSL Containers backend (`Purview.Containers.Wsl`): containers, images, runtime, networking, mounts, diagnostics. |
| `src/src/Docker` | Docker backend (`Purview.Containers.Docker`): the same containers on a Docker daemon, via Testcontainers. |
| `src/src/<Module>` | Service modules; each carries a bespoke `Sdk/README.md` that ships as the package README. |
| `src/tests` | TUnit unit and integration test projects (WSLC and Docker suites). |
| `samples/getting-started` | Runnable WSLC and Docker samples (`just sample-wsl`, `just sample-docker`). |
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
