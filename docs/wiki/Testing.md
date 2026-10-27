# Testing

How the test suite is organised, why it runs serially, and how to run a subset.

## Test categories

Test projects are discovered under `src/tests` and the SDK stamps every test assembly with a TUnit category:

| Project | Category | Runtime needed |
| --- | --- | --- |
| `*.UnitTests` | `Unit` | none — pure logic, parsing, configuration and wait-strategy units, some with in-process fakes. |
| `Wsl.*` and module `*.IntegrationTests` | `Integration` | a **WSLC host** — real containers on a shared WSLC session. |
| `Docker.IntegrationTests`, `Modules.Docker.IntegrationTests` | `Integration` | any **Docker daemon** — these target `net10.0`, so they also run on Linux. |

`.config/purview-build.json` keeps test discovery to unit projects (`TestPatterns` is
`*UnitTests.csproj`), so the shared Ubuntu pipeline never launches a Windows-targeted WSLC test app. A
local pipeline run is therefore runtime-free too. Container coverage is explicit instead:

- **Docker** — `integration-docker` in `pr.yml` runs the Docker contract and module suites on
  `ubuntu-latest` on every pull request.
- **WSLC contract** — `integration-wsl` in `pr.yml` runs `Wsl.IntegrationTests` on a `windows-2025`
  runner on every pull request.
- **Full WSLC matrix** — `.github/workflows/integration-wsl.yml` runs the contract suite and every
  service module on a `windows-2025` runner nightly (and on demand).

The suites retry transient failures at the **test-project** level through Microsoft.Testing.Platform's
retry extension (`--retry-failed-tests`). A retry restarts the test host, which gives WSLC a fresh session
and Testcontainers a fresh process; `--retry-failed-tests-delay 5s` lets the previous host release the
session VHD. Runner failures are still real failures: every integration job pins
`PURVIEW_CONTAINERS_BACKEND` and sets `PURVIEW_CONTAINERS_REQUIRE_BACKEND`, so a missing or incompatible
runtime fails the job instead of skipping every test.

> The shared `purview-dev/build` workflow runs on **`ubuntu-latest`**, so the pipeline builds the portable
> `net10.0` projects and the `net11.0-windows…` test projects on Linux. That works because
> `src/Directory.Build.props` sets `EnableWindowsTargeting=true`; without it the SDK reports `NETSDK1100`.
> See [Consumer Requirements](Consumer-Requirements.md) for what that workaround does and does not
> cover.

```powershell
just test                              # every discovered test project, retrying failed tests twice
just test '/*/*/*/*[Category=Unit]'     # unit tests only
just test '/*/*/*/*[Category=Integration]' --max-parallel-test-modules 1
```

Integration suites target whichever backend is selected. Automatic detection prefers WSLC and falls back
to Docker, so a WSLC host runs the WSLC suites and a Docker-only host runs the Docker ones; pin one with
`PURVIEW_CONTAINERS_BACKEND=wsl|docker` to fail loudly instead of falling back. The Docker suites
(`Docker.IntegrationTests` for the container contract, `Modules.Docker.IntegrationTests` for the nine
service modules) skip themselves when no daemon is reachable, and the WSLC suites skip themselves when the
host lacks the WSL Containers components. Set `PURVIEW_CONTAINERS_REQUIRE_BACKEND=wsl|docker` to turn that
skip into a failure — the CI integration jobs do. See [Backends: WSLC or Docker](Backends.md) for
consumer-facing setup and CI examples.

```powershell
just test src/tests/Docker.IntegrationTests/Docker.IntegrationTests.csproj                            # Docker contract
just test src/tests/Modules.Docker.IntegrationTests/Modules.Docker.IntegrationTests.csproj             # the nine modules on Docker
```

## Why test modules run serially

A WSLC session **exclusively locks its `storage.vhdx`**, and the lock is taken lazily on the first store
access rather than at session start. Running test assemblies in parallel therefore makes the losers fail with
`0x80070020`:

```text
The process cannot access the file because it is being used by another process.
```

`just test` passes `--max-parallel-test-modules 1` for that reason, and `.config/purview-build.json` sets
`Build:MaxTestParallelism` to `1` so the pipeline does not run test projects in parallel either. The runtime
verifies the store once and transparently falls back to an isolated per-process store, but running modules
serially keeps the warm shared image cache (no per-process re-pull) and is the fastest option.

Within a project, TUnit runs tests in parallel by default. The container suites cap that themselves:
`ModuleDockerTests` and each service-module suite run one test at a time (`[NotInParallel]`), and
`Wsl.IntegrationTests` uses a `ParallelLimiter` of two. That is what keeps Testcontainers' single resource
reaper and the 4 GB WSLC session VM from being stampeded on a cold run.

In Visual Studio, untick **Run Tests in Parallel** (or set *Maximum Parallel Test Projects* to 1) before
running the WSLC integration suites.

## Running the WSLC suites in CI

The WSLC half used to be manual-only because no hosted runner was assumed to have WSL Containers. That is
no longer true: GitHub's `windows-2025` runner is Windows Server 2025 with hardware nested virtualisation,
and `wsl --update --pre-release` installs a WSL build that includes `wslc`. The binary is not on `PATH`, so
the workflow adds `C:\Program Files\WSL` and calls it by absolute path.

Coverage is split for cost:

- **Every pull request**: `pr.yml` runs `Wsl.IntegrationTests` on `windows-2025` (the contract suite).
- **Nightly**: `.github/workflows/integration-wsl.yml` runs the contract suite and the nine service-module
  suites on `windows-2025`, one project at a time.
- **On demand**: the same workflow accepts `runner: self-hosted` to use a warm self-hosted Windows runner
  with the custom `wslc` label, and `scope: contract` to run only the contract suite.

Trigger a manual run from **Actions → Integration (WSL Containers) → Run workflow**, or:

```bash
gh workflow run "Integration (WSL Containers)" --ref main
gh run watch
```

### Hosted runner notes

Windows runners bill at 2× and the full module matrix pulls several GB of images on a cold runner, so the
full matrix is nightly rather than per-PR. `windows-2022` and Windows 10 do not support WSL Containers;
`windows-2025` is the only hosted option. The WSLC API surface is pinned by the
`Microsoft.WSL.Containers` package, so a hosted runner with a newer pre-release WSL may report the runtime
as incompatible. The workflow sets `PURVIEW_CONTAINERS_REQUIRE_BACKEND=wsl`, which turns that into a failed
job rather than a green run with every test skipped.

### Self-hosted runner prerequisites

Register a **Windows x64** runner for this repository (or organisation) with the labels
`self-hosted` and the custom **`wslc`** label, and install on that machine:

- Windows 11 (22H2 or later) or Windows Server 2025 with **WSL Containers**:
  `wsl --install --no-distribution`, verified with `wsl --version` and `wslc version`. Windows 10 and
  Windows Server 2022 do not support WSL Containers.
- The **.NET 11 SDK** the repository pins in `global.json` (`11.0.100-rc.2.26504.105`).

The job's first step prints `wsl --version` and `wslc version`, so a mis-provisioned runner fails
immediately instead of surfacing as container timeouts. `actionlint` is told about the custom label in
`.github/actionlint.yaml`.

## Expectations

- The `Wsl.IntegrationTests` module runs many real containers in one session and can take several
  minutes, because WSLC serialises container operations. Slow-test warnings while it runs are expected.
- Integration tests skip themselves when the host lacks the required WSL/WSLC components, so a machine
  without WSLC can still run the unit suites. Set `PURVIEW_CONTAINERS_REQUIRE_BACKEND` to fail instead.

## Verifying the consumer contract

`Wsl.UnitTests/ConsumerRequirementsTests.cs` guards the shape of the shipped library inside
the normal unit run: the Windows build targets `.NETCoreApp,Version=v10.0`, targets `Windows10.0.19041.0`
and declares only that OS platform. If the target framework ever drifts, that test fails — and
[Consumer Requirements](Consumer-Requirements.md), the package READMEs and the shipped
`buildTransitive` defaults all have to move with it.

The behavioural side is covered by `just verify-consumers`, which packs the packages and builds
throwaway consumer projects for every documented outcome (see
[Consumer Requirements](Consumer-Requirements.md#verifying-these-requirements)). It is deliberately
outside the `[Category=Unit]` filter because it packs and restores from nuget.org:

```powershell
just verify-consumers          # 21 consumer projects, all assertions
just verify-consumers -Keep    # same, keeping the generated projects for inspection
```

## Related

- [Architecture](Architecture.md) — session lifetime, the concurrency gate and the shared-store fallback.
- [Getting Started](Getting-Started.md) — running the tests locally.
