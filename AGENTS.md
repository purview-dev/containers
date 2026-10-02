# Agent Instructions

## Purpose and authority

This repository contains `Purview.Containers`, a Testcontainers-style library for .NET that runs throwaway
Linux containers for integration testing on **Microsoft WSL Containers (WSLC)** — the Windows runtime with
no Docker installation — or on **Docker** through Testcontainers, plus the service modules (`PostgreSql`,
`Redis`, `MsSql`, `RabbitMq`, `Azurite`, `Nats`, `MySql`).
`docs/wiki/Backends.md` is the consumer guide to choosing and configuring a backend; keep it up to date
whenever a target framework, a backend package or the `buildTransitive` assets change.

- This file is the repository-wide source of truth for AI agents. A more-specific `AGENTS.md` in a subtree, if
  one is ever added, takes precedence for that subtree.
- `.github/copilot-instructions.md` defers to this file.
- Follow explicit user instructions first, then the nearest applicable repository instructions, then
  established code patterns.
- Operate only in this repository unless the user explicitly expands the scope.
- Never read, copy, log, or commit secrets from excluded files, environment variables, user profiles, local
  configuration, test output, or registry credentials.

## Source of truth and layout

| Path | Purpose |
| --- | --- |
| `src/Containers.slnx` | Canonical solution for restore, build, test and pack |
| `src/src/Containers` | Umbrella package (`Purview.Containers`, `net10.0`): references `Core` and both backends; no code of its own |
| `src/src/Core` | Backend-neutral abstractions (`Purview.Containers.Core`, `net10.0`, namespace `Purview.Containers`): `IContainer`/`ContainerConfiguration`, `ContainerBuilder`, `ContainerBase`, `IContainerBackend`/`ContainerBackends`, wait strategies, images, networking, mounts, diagnostics |
| `src/src/Wsl` | WSL Containers backend (`Purview.Containers.Wsl`, multi-target `net10.0` facade + `net10.0-windows10.0.19041.0` implementation): `WslContainerBackend`, the shared session runtime, `WslContainer`, `WslContainerSession`, and the portable facade (`WslPayload`, `*.Facade.cs`) that loads the implementation at run time |
| `src/src/Docker` | Docker backend (`Purview.Containers.Docker`, `net10.0`): `DockerContainerBackend` + `DockerContainer` over Testcontainers |
| `src/src/<Module>` | Service modules (`Purview.Containers.<Module>`, `net10.0`); each is a thin layer over the abstractions and carries a bespoke `Sdk/README.md` |
| `src/src/<Project>/Sdk` | Package-only assets. `Sdk/README.md` is packed as the package README (suppressing the repo-root README); `Sdk/buildTransitive/**` ships MSBuild assets to consumers (the abstractions' backend registration, each backend package's own registration entry, and the WSL backend's consumer defaults and guards) |
| `src/tests` | TUnit unit (`*.UnitTests`) and container integration (`*.IntegrationTests`) projects: the WSLC suites need a WSLC host, `Docker.IntegrationTests` and `Modules.DockerIntegrationTests` need a Docker daemon |
| `docs/wiki` | User-facing documentation wiki, aggregated by the purview-dev website. `Backends.md` is the consumer guide to choosing and configuring WSLC or Docker |
| `samples/getting-started` | Runnable consumer-shaped samples (WSLC and Docker) built with the solution; `just sample-wsl` / `just sample-docker` |
| `mkdocs.yml` | Wiki site configuration (`docs_dir: docs/wiki`) |
| `src/Directory.Build.props` / `src/Directory.Build.targets` | Solution-wide SDK import, package metadata and the shared package icon |
| `purview-build.json` | Shared `Purview.Build` pipeline configuration: solution, test discovery/filter and the **exhaustive** pack-validation manifest |
| `Justfile` | Supported local workflow commands |
| `.agents` | Package-delivered skills, agents and prompts (read-only here) |
| `.purview/agent-sync.cache` | SDK-managed manifest recording which `.agents` files are already mirrored |

## Standard workflow

1. Read this file, inspect the working tree, and locate the implementation, tests, documentation and existing
   patterns relevant to the task.
2. Confirm behaviour from code and tests rather than relying on memory or documentation alone.
3. Make the smallest coherent change. Preserve public behaviour unless the task explicitly changes it.
4. Update tests for fixes and behaviour changes. Update documentation when public behaviour changes.
5. Run the narrowest meaningful validation first, then broader validation in proportion to risk.
6. Review the diff for unrelated edits, generated noise, compatibility risks, and missing docs or tests.

## Runtime invariants

- **One shared, process-wide session** (`WslContainerRuntime.Instance`), lazily started, named
  `wslc-{pid}-{random8}`. Container isolation comes from unique names and unique host ports, not from extra
  sessions.
- **Images are shared by default** (`StorageMode.Shared` → `%LOCALAPPDATA%\Purview\WslContainers\images`). A
  session exclusively locks its `storage.vhdx`; the runtime verifies the store once and falls back to an
  isolated per-process store on a sharing violation (`0x80070020`). Do not remove that fallback.
- **All session-mutating operations are serialised** through the runtime's gate (concurrent `Start` can fail
  with `0x8000FFFF`). Keep new lifecycle paths inside it.
- **Random host ports are native** (`windowsPort=0`) and read back from the container's mapped ports — never
  probe for a free port.
- **Default networking is `Bridged`**; only IPv4 loopback is mapped, and UDP is unsupported
  (`ContainerNotSupportedException`).
- **One backend registry per process** (`ContainerBackends`): backend packages register themselves through
  the generated module initializer, `ResolveAsync()` returns the pinned backend, the
  `PURVIEW_CONTAINERS_BACKEND`-selected backend, or the first *usable* registered backend in
  auto-priority order (`IContainerBackendPreference.AutoPriority`, lowest first; `wsl` is 0 and `docker`
  is 100, so WSLC is preferred when both are usable), and an empty/unusable registry throws
  `ContainerBackendUnavailableException` naming the fix. A named or pinned backend never silently falls
  back. A module must never reference a backend package — it resolves through the registry.
- **`DisposeAsync` is idempotent** and never terminates the shared session; a process-exit hook disposes the
  session so its name is released.
- **Secrets use `Secret`** and must never reach logs, names, `ToString()` or diagnostics (they are redacted).

## Consumer requirements and compatibility

- The packages split by framework: `Purview.Containers.Core` (abstractions) and the service modules target
  **`net10.0`** on any platform. `Purview.Containers.Wsl` is **multi-target**: `net10.0` (a portable
  facade) and `net10.0-windows10.0.19041.0` (the WSLC implementation). `src/src/Directory.Build.props`
  sets the `net10.0` subtree default and `Wsl.csproj` adds the Windows build; the WSLC tests keep the
  Windows target because they drive WSLC and therefore bind the implementation, while the Docker
  integration projects override to `net10.0` so they run on Linux. The contract, the
  failures that enforce it and the CI workarounds are documented in
  [Consumer Requirements](docs/wiki/Consumer-Requirements.md); update that page whenever a target
  framework, the `buildTransitive` defaults, the payload layout or the `PCC0001`/`PCC0002` guards change.
- `Purview.Containers.Wsl` ships `Sdk/buildTransitive/Purview.Containers.Wsl.{props,targets}` so consumers
  inherit `WindowsSdkPackageVersion`/`PlatformTarget` defaults, a clear error for an unsupported target
  framework (`PCC0001`) or a 32-bit Windows consumer (`PCC0002`), and — for a platform-neutral consumer
  on a Windows build host — a copy of the WSLC implementation payload (`wslc/`) that the `net10.0` facade
  loads at run time. Those assets are framework-agnostic, so NuGet never raises `NU1202` at restore time;
  the guards are the fail-fast path. Keep them, and keep the `RequiredContent` entries (including
  `payload/**`) that declare them. They belong to the WSL backend package only.
- `Purview.Containers.Core` ships `Sdk/buildTransitive/Purview.Containers.Core.{props,targets}`, which turn
  the `PurviewContainersBackends` list (each backend package appends its own entry) into a generated
  module initializer in the consuming assembly. That is how a package consumer's process discovers its
  backend without reflection or assembly scanning. A project-reference consumer (this repository's own
  tests) registers explicitly instead — `WslcTest.EnsureBackendRegistered()`.
- `src/Directory.Build.props` sets `EnableWindowsTargeting=true` because the shared CI agent is
  `ubuntu-latest`; removing it fails the pipeline with `NETSDK1100`.
- The project is **experimental**: keep the experimental notice in `README.md`, `docs/wiki/Home.md` and
  the package READMEs, and keep versions on a `-prerelease.N` suffix.

## Module rules

- A module supplies only defaults: image, ports, environment, module configuration (`WithXxx`), a readiness
  strategy, and connection-string/endpoint accessors. It must not duplicate runtime infrastructure.
- New modules follow the shape in [Contributing Modules](docs/wiki/Contributing-Modules.md): a
  `ContainerConfiguration`-derived record, a `ContainerBuilder<TBuilder, TContainer, TConfiguration>`
  subclass, and a container deriving from `ContainerBase` that exposes
  `GetConnectionString()`/endpoints built from `GetMappedPublicPort`. Take an `IContainerBackend` in the
  builder and container constructors and pass it through as `new MyContainer(configuration, Backend)` — a `null` backend is resolved on start via `ContainerBackends.ResolveAsync()`; a
  module package must never reference `Purview.Containers.Wsl` (that is what keeps it runnable on any
  backend and restorable on Linux).
- Prefer verifying the service for readiness (exec a readiness command or open a host client connection) over
  a bare TCP check. Where an image reports readiness too early (MySQL), a log match is wrong.
- SQL Server requires an explicit `AcceptLicense()` call; never accept licensing terms on the caller's behalf.

## Packaging and validation

- Every project under `src/src` sets `<IsPackable>true</IsPackable>` **literally** in the `.csproj`: the SDK
  resolves `IsPackable` by scanning the project file, and the package metadata/icon conditions in
  `src/Directory.Build.props` depend on it.
- Each package ships `lib/$(TFM)/<Assembly>.{dll,xml}`, `README.md` (from `Sdk/README.md`) and
  `purview-logo-light.png`. PDBs ship only in the `.snupkg`. MSBuild assets are per package and must be
  named after their own package id for NuGet to import them: `Purview.Containers.Core` ships
  `buildTransitive/Purview.Containers.Core.{props,targets}` (backend registration),
  `Purview.Containers.Wsl` ships `buildTransitive/Purview.Containers.Wsl.{props,targets}` (consumer
  defaults and guards) and `Purview.Containers.Docker` ships
  `buildTransitive/Purview.Containers.Docker.props` (its registration entry). The umbrella
  `Purview.Containers` ships none of its own; `Core`'s and the backends' assets reach its consumers
  transitively. See
  [Consumer requirements and compatibility](#consumer-requirements-and-compatibility).
- `PackValidation.RequireExplicitContent` defaults to `true`, so `purview-build.json`'s `RequiredContent` is
  the **exhaustive** manifest: a produced package with no rule, or a packed entry matched by no glob, fails
  validation. Adding or removing packaged content means updating that map.
- Validate with `just pack` and `just pipeline-pack-validate`.

## Testing

- Test assemblies are categorised by the SDK: `*.UnitTests` → `Unit`, `*.IntegrationTests` → `Integration`.
- `purview-build.json` filters the shared pipeline to `[Category=Unit]`, so it never needs a WSLC host. The
  **Docker** integration suites (`Docker.IntegrationTests`, `Modules.DockerIntegrationTests`) are the one
  exception: they target `net10.0` and run in the `.github/workflows/pr.yml` `integration-docker` job on
  `ubuntu-latest`, which is the standing proof that the library and modules work off Windows/WSL. Keep
  them portable; they remove the SDK's automatic `SharedTestingFramework` reference because that helper is
  WSLC/Windows-only. The **WSLC** suites still never run in the shared pipeline.
- WSLC integration tests run **serially** (`--max-parallel-test-modules 1` in the `Justfile`): a session
  exclusively locks its image-store VHD, so parallel modules fail with `0x80070020`.
- Use the shared `WslcTest` helper in `src/tests/SharedTestingFramework` for WSLC integration
  skip/availability checks; the Docker suites use their own `DockerTest` helper. Unit tests may use the
  modules' `BuildConfigurationForTesting()` internal hook.
- `Wsl.UnitTests/ConsumerRequirementsTests.cs` guards the published target framework of the Windows
  build (`.NETCoreApp,Version=v10.0` plus `Windows10.0.19041.0`), which the test project binds; keep it
  in step with [Consumer Requirements](docs/wiki/Consumer-Requirements.md).
- `just verify-consumers` packs and then builds throwaway consumer projects to assert the documented
  guard errors and workarounds. It restores from nuget.org, so it stays out of the `[Category=Unit]`
  filter and is a local/explicit step.

## Local commands

```text
just build                            # dotnet build (Debug)
just test '/*/*/*/*[Category=Unit]'   # unit tests only
just lint-check / just lint-fix       # CSharpier check / format
just pack                             # build + dotnet pack into ./artifacts
just verify-consumers                 # build throwaway consumers against the packed packages
just pipeline-pack-validate           # restore, build, lint, test, pack, validate
just scrub                            # reset bin/obj, clean, forced restore, build-server shutdown
```

Commit messages follow Conventional Commits enforced by the `commit-msg` lefthook
(`.config/lefthook.yml` → `npx commitlint`); allowed types are in `commitlint.config.mts`.

## Versioning and releases

- `package.json` is the authoritative release and package version. Do not diverge project versions by hand.
- `.github/workflows/pr.yml` builds and tests pull requests through the shared
  `purview-dev/build/.github/workflows/purview-build.yml`, with pack and validation enabled.
- `.github/workflows/release.yml` runs the shared release pipeline with `release-mode: NuGet` on a push to
  `main`.
- `.github/workflows/integration-wsl.yml` is a **manual** (`workflow_dispatch`) workflow that runs the
  WSLC integration suites on a self-hosted Windows runner with the custom `wslc` label (documented in
  [Testing](docs/wiki/Testing.md)); `.github/actionlint.yaml` declares that label. It is never attached to
  `pull_request` or `push`.
- The workflows pin `dotnet-version` to `global.json`'s `sdk.version`; keep them in sync, and keep
  `purview-build.json` pointing at `src/Containers.slnx`. The shared workflow runs on
  `ubuntu-latest`, which is why `EnableWindowsTargeting=true` must stay in `src/Directory.Build.props`.

## Completion checklist

Before handing work back:

- Confirm the requested behaviour and scope are satisfied, and only intended files changed.
- Review public API, package-content and dependency-direction implications.
- Update the affected package `Sdk/README.md`, the root `README.md`, `docs/wiki` (plus `_Sidebar.md` and
  `mkdocs.yml` for new pages), `AGENTS.md` and `purview-build.json` when the change affects them.
  `docs/wiki/Consumer-Requirements.md` is the contract every consumer-facing change has to keep
  accurate, and `just verify-consumers` is the check that proves it.
- Watch for the packaging traps: a new packable project missing `<IsPackable>true</IsPackable>` or a
  `RequiredContent` entry, and a new `Sdk/README.md` that does not describe its own package.
- Run the appropriate build, test, formatting and pack checks in proportion to risk.
- State exactly what validation ran. If a check was skipped or blocked, give the concrete reason and the
  remaining risk.
