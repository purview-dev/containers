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

```powershell
just test                              # every discovered test project
just test '/*/*/*/*[Category=Unit]'     # unit tests only
just test '/*/*/*/*[Category=Integration]' --max-parallel-test-modules 1
```

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

- The `WslContainers.IntegrationTests` module runs many real containers in one session and can take several
  minutes, because WSLC serialises container operations. Slow-test warnings while it runs are expected.
- Integration tests skip themselves when the host lacks the required WSL/WSLC components, so a machine
  without WSLC can still run the unit suites.
- `spikes/WslcSpikes` is a manual investigation harness (`dotnet run --project spikes/WslcSpikes -- sfull`,
  or `s1`..`s14` for individual behaviour probes); it is not part of the test run.

## Related

- [Architecture](Architecture.md) — session lifetime, the concurrency gate and the shared-store fallback.
- [Getting Started](Getting-Started.md) — running the tests locally.
