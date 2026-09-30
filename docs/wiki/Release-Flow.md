# Release Flow

How this repository builds, versions, validates and publishes `Purview.Containers.*`.

## Versioning

`package.json` at the repository root is the authoritative version (`1.0.0-prerelease.1` today). The SDK
applies that value to `Version` and `PackageVersion` for every project, so a release is a `package.json`
bump — never a manual project-file edit. `package.json` also carries the repository, homepage and issue URLs
that end up in each nuspec.

The project is **experimental**, so versions stay on a `-prerelease.N` suffix: consumers should pin an
exact version rather than float, and each bump can change the API or the
[consumer requirements](Consumer-Requirements.md).

## Local commands

The `Justfile` wraps the common steps:

| Command | What it does |
| --- | --- |
| `just build` | `dotnet build src/WSLTestContainers.slnx` (Debug). |
| `just test` | `dotnet test` across the solution, one test module at a time (see [Testing](Testing.md)). |
| `just lint-check` / `just lint-fix` | CSharpier check / format over the repository root. |
| `just pack` | Build (Debug) and `dotnet pack` into `./artifacts`. |
| `just verify-consumers` | Pack, then build throwaway consumer projects that assert the published consumer contract ([Consumer Requirements](Consumer-Requirements.md)). |
| `just scrub` | Delete `bin`/`obj`, clean, re-restore with `--force-evaluate`, and shut down the build server. |
| `just pipeline-pack-validate` | Shared pipeline: restore, build, lint, test, pack and **validate** the packages, without publishing. |

## The shared pipeline

All pipeline recipes first install the `Purview.Build` tool into `.tools/purview-build` and then run it
against `purview-build.json`:

| Recipe | Pipeline arguments |
| --- | --- |
| `just pipeline-pr` | default (full PR pipeline) |
| `just pipeline-build` | `--Build:RunTests=false --Release:Mode=None` |
| `just pipeline-tests` | `--Build:RunTests=true --Release:Mode=None` |
| `just pipeline-pack-validate` | `--Build:RunPack=true --Build:ValidatePack=true --Release:Mode=None` |
| `just pipeline-local-release` | `--Release:Mode=LocalNuGet` |
| `just pipeline-release` | `--Release:Mode=NuGet` |

The pipeline cleans `artifacts/` before it runs, builds in **Release**, lints with CSharpier, runs the
test filter from `purview-build.json` (`[Category=Unit]`, so no WSLC host is needed), packs, and then
validates the produced packages against the exhaustive `RequiredContent` manifest. Any module failure fails
the run with the failing module's output.

## GitHub Actions

- `.github/workflows/pr.yml` — pull requests build and test via the shared
  `purview-dev/build/.github/workflows/purview-build.yml`, with pack and validation enabled.
- `.github/workflows/release.yml` — a push to `main` calls `purview-dev/build/.github/workflows/purview-release.yml`
  with `release-mode: NuGet`, which packs, publishes to NuGet and creates the `v<version>` GitHub release.

Both workflows pin `dotnet-version` to the SDK in `global.json` (`11.0.100-rc.1.26425.128`); keep them in
sync when the SDK is bumped, and keep `purview-build.json` pointing at `src/WSLTestContainers.slnx`.

The shared workflow runs on **`ubuntu-latest`**, so the Linux agent builds the portable `net10.0` projects
and the `net11.0-windows10.0.19041.0` test projects. That only works because `src/Directory.Build.props`
sets `EnableWindowsTargeting=true`; removing it fails the pipeline with `NETSDK1100`. The agent has no WSL
Containers, which is why the pipeline is filtered to `[Category=Unit]` — see
[Consumer Requirements](Consumer-Requirements.md) and [Testing](Testing.md).

## Related

- [Packaging](Packaging.md) — the package contents and the validation manifest.
- [Contributing](Contributing.md) — the local workflow before raising a PR.
