# Agent Instructions

## Purpose and authority

This repository contains `Purview.WslContainers`, a **WSLC-native** Testcontainers-style library for .NET:
throwaway Linux containers for integration testing on **Microsoft WSL Containers (WSLC)**, with no Docker
installation, plus the service modules (`PostgreSql`, `Redis`, `MsSql`, `RabbitMq`, `Azurite`, `Nats`,
`MySql`).

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
| `src/WSLTestContainers.slnx` | Canonical solution for restore, build, test and pack |
| `src/src/WslContainers` | Core runtime (`Purview.WslContainers`): `ContainerBuilder`, `WslContainer`, runtime, sessions, images, networking, mounts, wait strategies, diagnostics |
| `src/src/<Module>` | Service modules; each is a thin layer over the core and carries a bespoke `Sdk/README.md` |
| `src/src/<Project>/Sdk` | Package-only assets. `Sdk/README.md` is packed as the package README (suppressing the repo-root README); `Sdk/buildTransitive/**` would ship MSBuild assets |
| `src/tests` | TUnit unit (`*.UnitTests`) and WSLC integration (`*.IntegrationTests`) projects |
| `spikes/WslcSpikes` | Phase 0 investigation harness (`s1`..`s14` probes); not part of the test run |
| `docs/wiki` | User-facing documentation wiki, aggregated by the purview-dev website |
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
  (`WslContainerNotSupportedException`).
- **`DisposeAsync` is idempotent** and never terminates the shared session; a process-exit hook disposes the
  session so its name is released.
- **Secrets use `Secret`** and must never reach logs, names, `ToString()` or diagnostics (they are redacted).

## Module rules

- A module supplies only defaults: image, ports, environment, module configuration (`WithXxx`), a readiness
  strategy, and connection-string/endpoint accessors. It must not duplicate runtime infrastructure.
- New modules follow the shape in [Contributing Modules](docs/wiki/Contributing-Modules.md): a
  `ContainerConfiguration`-derived record, a `ContainerBuilder<TBuilder, TContainer, TConfiguration>`
  subclass, and a container exposing `GetConnectionString()`/endpoints built from `GetMappedPublicPort`.
- Prefer verifying the service for readiness (exec a readiness command or open a host client connection) over
  a bare TCP check. Where an image reports readiness too early (MySQL), a log match is wrong.
- SQL Server requires an explicit `AcceptLicense()` call; never accept licensing terms on the caller's behalf.

## Packaging and validation

- Every project under `src/src` sets `<IsPackable>true</IsPackable>` **literally** in the `.csproj`: the SDK
  resolves `IsPackable` by scanning the project file, and the package metadata/icon conditions in
  `src/Directory.Build.props` depend on it.
- Each package ships `lib/$(TFM)/<Assembly>.{dll,xml}`, `README.md` (from `Sdk/README.md`) and
  `purview-logo-light.png`. PDBs ship only in the `.snupkg`.
- `PackValidation.RequireExplicitContent` defaults to `true`, so `purview-build.json`'s `RequiredContent` is
  the **exhaustive** manifest: a produced package with no rule, or a packed entry matched by no glob, fails
  validation. Adding or removing packaged content means updating that map.
- Validate with `just pack` and `just pipeline-pack-validate`.

## Testing

- Test assemblies are categorised by the SDK: `*.UnitTests` → `Unit`, `*.IntegrationTests` → `Integration`.
- `purview-build.json` filters pipeline runs to `[Category=Unit]`, so the shared pipeline never needs a WSLC
  host; keep it that way unless the task requires real containers.
- WSLC integration tests run **serially** (`--max-parallel-test-modules 1` in the `Justfile`): a session
  exclusively locks its image-store VHD, so parallel modules fail with `0x80070020`.
- Use the shared `WslcTest` helper in `src/tests/SharedTestingFramework` for integration skip/availability
  checks. Unit tests may use the modules' `BuildConfigurationForTesting()` internal hook.

## Local commands

```text
just build                            # dotnet build (Debug)
just test '/*/*/*/*[Category=Unit]'   # unit tests only
just lint-check / just lint-fix       # CSharpier check / format
just pack                             # build + dotnet pack into ./artifacts
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
- Both workflows pin `dotnet-version` to `global.json`'s `sdk.version`; keep them in sync, and keep
  `purview-build.json` pointing at `src/WSLTestContainers.slnx`.

## Completion checklist

Before handing work back:

- Confirm the requested behaviour and scope are satisfied, and only intended files changed.
- Review public API, package-content and dependency-direction implications.
- Update the affected package `Sdk/README.md`, the root `README.md`, `docs/wiki` (plus `_Sidebar.md` and
  `mkdocs.yml` for new pages), `AGENTS.md` and `purview-build.json` when the change affects them.
- Watch for the packaging traps: a new packable project missing `<IsPackable>true</IsPackable>` or a
  `RequiredContent` entry, and a new `Sdk/README.md` that does not describe its own package.
- Run the appropriate build, test, formatting and pack checks in proportion to risk.
- State exactly what validation ran. If a check was skipped or blocked, give the concrete reason and the
  remaining risk.
