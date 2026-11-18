# Purview.Containers

Purview.Containers is a Testcontainers-style .NET library for integration tests that run throwaway
Linux containers on **Microsoft WSL Containers (WSLC)**, with no Docker installation, or on **Docker**
through Testcontainers. The same test code runs on either runtime. The WSLC backend is built directly
against the `Microsoft.WSL.Containers` 3.0.5 managed package, with a bounded `wslc` CLI fallback only for
capabilities the SDK does not expose; see [Capabilities and Cancellation](guide/Backend-Capabilities.md).

The packages are published under the `Purview.Containers.*` package IDs.

## Documentation

| Section | Page | What it answers |
| --- | --- | --- |
| Start | [Getting Started](Getting-Started.md) | Install the packages and run a first container. |
| Start | [Using in Your Tests (auto)](Using-in-Your-Tests.md) | Zero-configuration tests that pick WSLC or Docker themselves. |
| Start | [Backends: WSLC or Docker](Backends.md) | Choosing, pinning and troubleshooting a backend, locally and in CI. |
| Start | [Consumer Requirements](Consumer-Requirements.md) | Target frameworks, host requirements, and the build errors that enforce them. |
| Start | [Modules](Modules.md) | The nine service modules, their defaults and readiness checks. |
| Guide | [Lifecycle](guide/Lifecycle.md) | Start, stop, dispose and cleanup semantics. |
| Guide | [Wait Strategies](guide/Wait-Strategies.md) | Composable readiness checks. |
| Guide | [Networking](guide/Networking.md) | Ports, bridged networking, aliases and host loopback. |
| Guide | [Connection Strings](guide/Connection-Strings.md) | Connection strings and endpoints from a running container. |
| Guide | [Capabilities and Cancellation](guide/Backend-Capabilities.md) | Parity APIs, SDK and CLI boundaries, timeouts and cancellation. |
| Contributing | [Contributing](contributing/README.md) | Build, lint, test and pack locally. |
| Contributing | [Contributing Modules](Contributing-Modules.md) | Adding a new service module. |
| Contributing | [Architecture](contributing/Architecture.md) | How the packages, backends and runtime fit together. |
| Contributing | [Testing](contributing/Testing.md) | Test tiers, the serial-run rule and the CI suites. |
| Contributing | [Packaging](contributing/Packaging.md) | What each package ships and how the manifest is validated. |
| Contributing | [Release Flow](contributing/Release-Flow.md) | Versioning, the pipeline and cutting a release. |

## Packages

| Package | Purpose |
| --- | --- |
| `Purview.Containers` | The umbrella: references `Purview.Containers.Core` and both backends, so one reference runs the same tests on WSLC on Windows and Docker elsewhere. No code of its own. |
| `Purview.Containers.Core` | Backend-neutral abstractions: the container contract, builders, wait strategies, images, networking, mounts, diagnostics, backend selection (namespace `Purview.Containers`). |
| `Purview.Containers.Wsl` | The WSL Containers backend: sessions, images, ports, mounts, wait strategies, logs/exec, diagnostics for WSLC. |
| `Purview.Containers.Docker` | The Docker backend: the same containers on any reachable Docker daemon, driven by Testcontainers. |
| `Purview.Containers.PostgreSql` | PostgreSQL container (`postgres:17`), `pg_isready` readiness, Npgsql connection string. |
| `Purview.Containers.Redis` | Redis-compatible container (`redis:7`), `redis-cli ping` readiness; also usable with Valkey/Garnet. |
| `Purview.Containers.MsSql` | SQL Server container (`mssql/server:2025-latest`), host-side `SqlClient` readiness, explicit EULA acceptance. |
| `Purview.Containers.RabbitMq` | RabbitMQ container (`rabbitmq:3-management`), AMQP + management endpoints. |
| `Purview.Containers.Azurite` | Azure Storage emulator (`azure-storage/azurite`), blob/queue/table endpoints. |
| `Purview.Containers.Nats` | NATS broker (`nats:2`), client + monitoring endpoints. |
| `Purview.Containers.MySql` | MySQL container (`mysql:8`), host-side `MySqlConnector` readiness. |
| `Purview.Containers.AzureKeyVaultEmulator` | Azure Key Vault emulator (`jamesgoulddev/azure-keyvault-emulator`), secrets/keys/certificates endpoints and certificate-pinned Azure SDK clients. |
| `Purview.Containers.AzureServiceBusEmulator` | Azure Service Bus emulator (`azure-messaging/servicebus-emulator`) with its SQL Server sidecar, AMQP and management connection strings, explicit EULA acceptance. |

## Feature highlights

- **One shared, process-wide session** — a stable storage path gives a warm image cache
  (`StorageMode.Shared`), and the runtime transparently falls back to an isolated per-process store when a
  concurrent process holds the shared store VHD. The image store defaults to
  `%LOCALAPPDATA%\Purview\WslContainers\images` and can be moved with `PURVIEW_CONTAINERS_STORAGE_PATH` or
  `WslContainerRuntimeOptions.StoragePath`.
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

- **WSL Containers backend:** an **x64** Windows host where WSL 2 runs (Windows 11, Windows 10 version
  2004 or later, or Windows Server 2025) with virtualisation enabled and **WSL 3.0.5 or later** from the
  stable channel (`wsl --install --no-distribution`, or `wsl --update`). ARM64 is not supported. See
  [Host requirements](Consumer-Requirements.md#host-requirements-wsl-containers-backend).
- **Docker backend:** any reachable Docker daemon (Docker Desktop, Docker Engine in WSL2, or a CI runner).
  Verify with `docker info`.
- .NET SDK 11 to build (the repository pins `11.0.100` in `global.json`).
- A consuming project must be a **.NET 10 or .NET 11** project when it uses the **WSL Containers backend**
  (**.NET 8 and .NET 9 are not supported**). The recommended shape is platform-neutral (`net10.0` or
  `net11.0`): it binds the portable facade and gets automatic WSLC-or-Docker selection. A Windows target
  framework (`net10.0-windows10.0.19041.0` or `net11.0-windows10.0.19041.0`, x64 only) binds the
  implementation directly. The abstractions and the service modules target `net10.0` and `net11.0` and are
  portable, but they need a backend package to run — see
  [Backends: WSLC or Docker](Backends.md) for the side-by-side setup. The full contract, the exact errors
  raised when it is not met, and the `EnableWindowsTargeting` workaround for non-Windows CI agents are in
  [Consumer Requirements](Consumer-Requirements.md).

```powershell
wsl --version     # WSL 3.0.5 or later (run 'wsl --update' if older)
wslc version      # ships with WSL (%ProgramFiles%\WSL\wslc.exe); e.g. 3.0.5.0
```

The library reports missing prerequisites through `WslContainerRuntime.GetInfoAsync()`; it never installs or
updates WSL on its own.

## Choosing a backend

Two backends implement the same API. The zero-choice default is the umbrella `Purview.Containers` (see the
Packages table above): one reference brings both backends and automatic selection — WSLC on Windows,
Docker elsewhere. Reference a single backend only when you want to fix the runtime. Automatic detection is
the default; pin one in code or from the environment:

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
| `src/Containers.slnx` | Canonical solution for restore, build, test and pack. |
| `src/src/Containers` | Umbrella package (`Purview.Containers`): references Core and both backends; no code of its own. |
| `src/src/Core` | Backend-neutral abstractions (`Purview.Containers.Core`, namespace `Purview.Containers`): the container contract, builders and backend selection. |
| `src/src/Wsl` | WSL Containers backend (`Purview.Containers.Wsl`): containers, images, runtime, networking, mounts, diagnostics. |
| `src/src/Docker` | Docker backend (`Purview.Containers.Docker`): the same containers on a Docker daemon, via Testcontainers. |
| `src/src/<Module>` | Service modules; each carries a bespoke `Sdk/README.md` that ships as the package README. |
| `src/tests` | TUnit unit and integration test projects (WSLC and Docker suites). |
| `samples/getting-started` | Runnable WSLC and Docker samples (`just sample-wsl`, `just sample-docker`). |
| `docs/wiki` | This wiki, aggregated by the purview-dev website. |
| `.config/purview-build.json` | Shared `Purview.Build` pipeline configuration, including the exhaustive pack manifest. |
| `skills` | The agent skills this repository authors and publishes (`npx skills add purview-dev/containers`). |
