# Recipes for Purview.Containers. Run `just` for the list.
#
# The shared recipes come from .build/just/, copied verbatim from the purview-buildsdk-justfile
# skill. Change them there, not here; this file holds what belongs to this repository.

set quiet

# Let a definition here override a module's default variable or no-op hook.
set allow-duplicate-variables
set allow-duplicate-recipes

import ".build/just/index.just"

solution_file := "src/Containers.slnx"

# The test runner treats "no test ran" (exit 8) as success, so a filter that matches nothing in one
# test project - the WSLC suites under a Unit filter, for example - does not fail the run.
export TESTINGPLATFORM_EXITCODE_IGNORE := env("TESTINGPLATFORM_EXITCODE_IGNORE", "8")

[private]
default:
    just --list

# -----------------------------------------------------------------------------
# Build and Test
# -----------------------------------------------------------------------------

# Overrides the shared `test` on purpose. The test projects must run one at a time
# (--max-parallel-test-modules 1): a WSLC session exclusively locks its image-store VHD, so parallel
# test assemblies contend for the shared store and fail with 0x80070020 ("file is being used by
# another process"). Override by appending the flag, e.g.
#
#   just test '/*/*/*/*/' --max-parallel-test-modules 4
#
# Failures are retried at the test-project level by Microsoft.Testing.Platform's retry extension. It
# restarts the test host for each attempt, which also gives WSLC a fresh session; the delay lets the
# previous host release the session VHD. --results-directory-layout per-module (.NET 11) gives every
# test application its own report folder, so one module's report cannot overwrite another's.
[doc('Run the tests matching a tree-node filter, one test project at a time (default: every test)')]
[group('Build and Test')]
test filter=test_filter *args:
    echo "==> Testing {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ YELLOW }}{{ build_configuration }}{{ NORMAL }}) with filter {{ GREEN }}{{ filter }}{{ NORMAL }}, one test project at a time"
    dotnet test --solution "{{ solution_file }}" --configuration "{{ build_configuration }}" --max-parallel-test-modules 1 --treenode-filter "{{ filter }}" --retry-failed-tests 2 --retry-failed-tests-delay 5s --retry-failed-tests-max-percentage 50 --results-directory-layout per-module {{ args }}

# Verifies the documented consumer requirements (docs/wiki/Consumer-Requirements.md) by packing and
# then building throwaway consumer projects against the produced packages: the supported target
# frameworks, the shipped buildTransitive defaults, the PCC0001/PCC0002 guard errors (including the
# x64-only rule) and the payload the portable facade loads. Slower than unit tests: it packs and
# restores from nuget.org.
[doc('Pack, then build throwaway consumer projects that assert the published consumer contract')]
[group('Build and Test')]
verify-consumers *args: pack
    pwsh -NoProfile -File scripts/verify-consumers.ps1 {{ args }}

# -----------------------------------------------------------------------------
# Samples
# -----------------------------------------------------------------------------

# Runs the WSLC getting-started sample: needs WSL Containers, starts a real container
[group('Samples')]
sample-wsl:
    echo "Running {{ BLUE }}samples/getting-started/WslSample{{ NORMAL }} (needs WSL Containers)..."
    dotnet run --project samples/getting-started/WslSample/WslSample.csproj

# Runs the auto (zero-config) getting-started sample: picks WSL Containers on Windows or Docker elsewhere
[group('Samples')]
sample-auto:
    echo "Running {{ BLUE }}samples/getting-started/AutoSample{{ NORMAL }} (WSL Containers on Windows, Docker elsewhere)..."
    dotnet run --project samples/getting-started/AutoSample/AutoSample.csproj

# Runs the Docker getting-started sample: needs a reachable Docker daemon, starts a real container
[group('Samples')]
sample-docker:
    echo "Running {{ BLUE }}samples/getting-started/DockerSample{{ NORMAL }} (needs a Docker daemon)..."
    dotnet run --project samples/getting-started/DockerSample/DockerSample.csproj

# -----------------------------------------------------------------------------
# Utilities
# -----------------------------------------------------------------------------

# Opens the source folder in Visual Studio Code
[group('Utilities')]
code:
    echo "Opening {{ BLUE }}src/{{ NORMAL }} in Visual Studio Code..."
    code "src/"
