# Testing

How the test suite is organised, why it runs serially, and how to run a subset.

## Test categories

Test projects are discovered under `src/tests` and the SDK stamps every test assembly with a TUnit category:

| Project suffix | Category | Needs WSLC |
| --- | --- | --- |
| `*.UnitTests` | `Unit` | No — pure logic, parsing, configuration and wait-strategy units, some with in-process fakes. |
| `*.IntegrationTests` | `Integration` | Yes — real containers on a shared WSLC session. |

`purview-build.json` filters the pipeline run to `/*/*/*/*[Category=Unit]`, so the shared pipeline never
starts containers. Run integration projects explicitly when you have a WSLC host.

> The shared `purview-dev/build` workflow runs on **`ubuntu-latest`**, so the pipeline builds the portable
> `net10.0` projects and the `net11.0-windows…` test projects on Linux. That works because
> `src/Directory.Build.props` sets `EnableWindowsTargeting=true`; without it the SDK reports `NETSDK1100`.
> See [Consumer Requirements](Consumer-Requirements.md) for what that workaround does and does not
> cover.

```powershell
just test                              # every discovered test project
just test '/*/*/*/*[Category=Unit]'     # unit tests only
just test '/*/*/*/*[Category=Integration]' --max-parallel-test-modules 1
```

Integration suites target whichever backend is selected. Automatic detection prefers WSLC and falls back
to Docker, so a WSLC host runs the WSLC suites and a Docker-only host runs the Docker ones; pin one with
`PURVIEW_CONTAINERS_BACKEND=wsl|docker` to fail loudly instead of falling back. The Docker suites
(`Docker.IntegrationTests` for the container contract, `Modules.DockerIntegrationTests` for the seven
service modules) skip themselves when no daemon is reachable, and the WSLC suites skip themselves when the
host lacks the WSL Containers components. See [Backends: WSLC or Docker](Backends.md) for consumer-facing
setup and CI examples.

## Why test modules run serially

A WSLC session **exclusively locks its `storage.vhdx`**, and the lock is taken lazily on the first store
access rather than at session start. Running test assemblies in parallel therefore makes the losers fail with
`0x80070020`:

```text
The process cannot access the file because it is being used by another process.
```

`just test` passes `--max-parallel-test-modules 1` for that reason. The runtime verifies the store once and
transparently falls back to an isolated per-process store, but running modules serially keeps the warm shared
image cache (no per-process re-pull) and is the fastest option.

In Visual Studio, untick **Run Tests in Parallel** (or set *Maximum Parallel Test Projects* to 1) before
running the WSLC integration suites.

## Expectations

- The `Wsl.IntegrationTests` module runs many real containers in one session and can take several
  minutes, because WSLC serialises container operations. Slow-test warnings while it runs are expected.
- Integration tests skip themselves when the host lacks the required WSL/WSLC components, so a machine
  without WSLC can still run the unit suites.
- `spikes/WslcSpikes` is a manual investigation harness (`dotnet run --project spikes/WslcSpikes -- sfull`,
  or `s1`..`s14` for individual behaviour probes); it is not part of the test run.

## Verifying the consumer contract

`Wsl.UnitTests/ConsumerRequirementsTests.cs` guards the shape of the shipped library inside
the normal unit run: the assembly targets `.NETCoreApp,Version=v11.0`, targets `Windows10.0.19041.0`
and declares only that OS platform. If the target framework ever drifts, that test fails — and
[Consumer Requirements](Consumer-Requirements.md), the package READMEs and the shipped
`buildTransitive` defaults all have to move with it.

The behavioural side is covered by `just verify-consumers`, which packs the packages and builds
throwaway consumer projects for every documented outcome (see
[Consumer Requirements](Consumer-Requirements.md#verifying-these-requirements)). It is deliberately
outside the `[Category=Unit]` filter because it packs and restores from nuget.org:

```powershell
just verify-consumers          # 16 consumer projects, all assertions
just verify-consumers -Keep    # same, keeping the generated projects for inspection
```

## Related

- [Architecture](Architecture.md) — session lifetime, the concurrency gate and the shared-store fallback.
- [Getting Started](Getting-Started.md) — running the tests locally.
