# Release Flow

How this repository builds, versions, validates and publishes `Purview.Containers.*`.

## Versioning

`package.json` at the repository root is the authoritative version. The SDK
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
| `just build` | `dotnet build src/Containers.slnx` (Debug). |
| `just test` | `dotnet test` across the solution, one test module at a time (see [Testing](Testing.md)). |
| `just lint-check` / `just lint-fix` | CSharpier check / format over the repository root. |
| `just pack` | Build (Debug) and `dotnet pack` into `./artifacts`. |
| `just verify-consumers` | Pack, then build throwaway consumer projects that assert the published consumer contract ([Consumer Requirements](Consumer-Requirements.md)). |
| `just scrub` | Delete `bin`/`obj`, clean, re-restore with `--force-evaluate`, and shut down the build server. |
| `just pipeline-pack-validate` | Shared pipeline: restore, build, lint, test, pack and **validate** the packages, without publishing. |

## The shared pipeline

All pipeline recipes first run `dotnet tool restore` (installing the pinned `Purview.Build` from
`.config/dotnet-tools.json`) and then run it against `purview-build.json`:

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
- `.github/workflows/release.yml` — a push to a **`release/**` branch** (or a manual `workflow_dispatch`)
  calls `purview-dev/build/.github/workflows/purview-release.yml` with `release-mode: NuGet`. It evaluates
  release eligibility and, when eligible, builds, tests, packs, publishes to NuGet and creates the
  `v<version>` tag and GitHub release. A push to `main` does **not** release.

Both workflows pin `dotnet-version` to the SDK in `global.json`; keep them in sync when the SDK is bumped,
and keep `purview-build.json` pointing at `src/Containers.slnx`.

## Cutting a release

1. Bump `version` in `package.json` on a branch (keep the `-prerelease.N` suffix), and merge it into
   `main` through a pull request.
2. Push a release branch named after the version from `main`. The convention is `release/v<major>.<minor>.<N>`,
   where `N` is the prerelease number, for example `release/v1.0.9` for `1.0.0-prerelease.9`:

   ```bash
   git switch main && git pull
   git push origin main:refs/heads/release/v1.0.9
   ```

3. The Release workflow runs on that push. Each release is a new `package.json` version and a new
   release branch.

Check the decision locally before pushing with `dotnet tool run purview-build -- release-explain`. It runs
nothing and publishes nothing; it reports the verdict and the rule that decided it.

### Eligibility

`.config/purview-build.json` selects the **`TrunkReservesMinor`** policy: `main` is the trunk, and
`release/*` branches are the stable and servicing refs that publish. The tool evaluates these rules:

| Rule | Checks |
| --- | --- |
| `REL001` | The version is valid for NuGet. |
| `REL002` | No `v<version>` tag exists yet. A version that has already been released is **skipped**, so every release needs a new `package.json` version. |
| `REL003` | Constrains stable (non-prerelease) versions; prereleases pass. |
| `REL004` | Constrains versions with a non-zero PATCH component. |
| `REL005` | The version is consistent with what already exists on its `<major>.<minor>` line. |

The workflow fails on a `Fail` verdict, logs a notice and publishes nothing on `Skip`, and publishes only
on `Release`.

The shared workflow runs on **`ubuntu-latest`**, so the Linux agent builds the portable `net10.0` projects
and the `net11.0-windows10.0.19041.0` test projects. That only works because `src/Directory.Build.props`
sets `EnableWindowsTargeting=true`; removing it fails the pipeline with `NETSDK1100`. The agent has no WSL
Containers, which is why the pipeline is filtered to `[Category=Unit]` — see
[Consumer Requirements](Consumer-Requirements.md) and [Testing](Testing.md).

## Related

- [Packaging](Packaging.md) — the package contents and the validation manifest.
- [Contributing](Contributing.md) — the local workflow before raising a PR.
