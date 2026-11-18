---
name: purview-containers-consumer-requirements
description: >-
  Use when a project consuming Purview.Containers will not build - PCC0001 or PCC0002, CS1705 for
  Microsoft.Windows.SDK.NET, CS0234/CS0246 for Microsoft.WSL types, NETSDK1100 on a Linux build agent
  - or when choosing the target framework and platform for a consumer of the WSL Containers backend.
metadata:
  product: Purview.Containers
  repository: purview-dev/containers
---

# Consumer requirements

## Which package requires what

| Package | Target frameworks | Notes |
| --- | --- | --- |
| `Purview.Containers` | `net10.0` and `net11.0`, any platform | the umbrella: `Core` plus both backends |
| `Purview.Containers.Core` | `net10.0` and `net11.0`, any platform | backend-neutral abstractions |
| `Purview.Containers.<Module>` | `net10.0` and `net11.0`, any platform | service modules; restore anywhere, need a backend to run |
| `Purview.Containers.Wsl` | `net10.0`, `net11.0`, `net10.0-windows10.0.19041.0` and `net11.0-windows10.0.19041.0` | portable facades plus the Windows implementations. **Everything below is its contract.** |
| `Purview.Containers.Docker` | `net10.0` and `net11.0`, any platform | needs a reachable Docker daemon, not a Windows target framework |

A consumer of any backend other than WSL needs only a portable `net10.0` or `net11.0` project. .NET 8 and
.NET 9 are unsupported.

WSLC features missing from the managed SDK (entrypoint/user/labels, reuse, copy
and Dockerfile builds) additionally require the `wslc` CLI that ships with WSL (found on PATH or at
`%ProgramFiles%\WSL\wslc.exe`); networks/aliases, Linux capabilities and
host loopback use the managed SDK 3.0.5. The ordinary SDK-only path is unchanged.
Use `purview-containers-capabilities` for the feature/cancellation boundary.

## Most projects need nothing

```xml
<TargetFramework>net10.0</TargetFramework>
```

That binds the portable facade and gets automatic backend selection — WSLC on a Windows host, Docker
everywhere else. It is the recommended choice, and it is why a Windows developer and a Linux CI
runner can share one test project.

A Windows-targeting project binds the implementation directly, with no run-time load:

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
  <PlatformTarget>x64</PlatformTarget>                      <!-- x64 only; defaulted for you -->
  <WindowsSdkPackageVersion>10.0.26100.80</WindowsSdkPackageVersion>  <!-- defaulted for you -->
</PropertyGroup>
```

The last two lines are optional — the package ships MSBuild defaults — but stating them makes the
requirement visible to the next person reading the project file, and the defaults only fill in a
value you have not chosen.

## The five requirements

| # | Requirement | Why |
| --- | --- | --- |
| 1 | **.NET 10 or .NET 11** (.NET 8 and .NET 9 are unsupported) | the package ships `lib/net10.0/` and `lib/net11.0/` (facades) plus `lib/net10.0-windows10.0.19041/` and `lib/net11.0-windows10.0.19041/` (implementations) |
| 2 | **A Windows target framework that names the OS version** — *only* if you want the implementation bound directly | the `Microsoft.WSL.Containers` projection ships Windows assets only |
| 3 | **x64** — `PlatformTarget` `x64` (the default), or the `win-x64` `RuntimeIdentifier` | the implementation is compiled for x64 and only the `win-x64` native `wslcsdk.dll` is deployed; ARM64 and x86 are not supported |
| 4 | **`WindowsSdkPackageVersion` ≥ `10.0.26100.80`** | the projection is compiled against `Microsoft.Windows.SDK.NET` `10.0.26100.79`, and the pack the SDK resolves for a `10.0.19041.0` target framework is older |
| 5 | **A host that can run WSL Containers**, to actually run anything (see below) | the library drives WSLC; it never installs or updates it for you |

### Host requirements

- **x64 only.** A non-x64 Windows-target consumer fails the build with `PCC0002`; a platform-neutral
  consumer in a non-x64 process sees `wsl` reported unavailable ("supports x64 processes only"), so
  automatic selection falls through to Docker.
- **WSL 3.0.5 or later** from the normal stable channel — `wsl --install --no-distribution` for a new
  install, or `wsl --update`. The `--pre-release` channel is not needed. The floor follows the bundled
  `Microsoft.WSL.Containers` SDK (3.0.5) and is checked at run time.
- **Windows wherever WSL 2 runs**: Windows 11, Windows 10 version 2004 (build 19041) or later with
  current updates, or Windows Server 2025. The library adds no Windows-version check; WSL reports an
  unsupported Windows as `WSL_E_OS_NOT_SUPPORTED`.
- **Virtualisation** enabled in firmware plus the Virtual Machine Platform feature
  (`wsl --install --no-distribution` enables it; restart required).
- **`wslc`** ships with WSL and is needed only for the CLI-backed features; it is found on PATH or at
  `%ProgramFiles%\WSL\wslc.exe`.

Tested on Windows 11 23H2 and Windows Server 2025; Windows 10 and Windows Server 2022 are untested.
`GetInfoAsync()` and `ContainerBackendUnavailableException` name the fix for each shortfall (for
example "Run 'wsl --update'").

### Always spell out the Windows version

```xml
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>  <!-- correct -->
<TargetFramework>net10.0-windows</TargetFramework>              <!-- fails -->
```

`net10.0-windows` resolves its platform version to **7.0**, older than the `10.0.19041.0` the
implementation was built against, so NuGet selects no compile assets at all. (The
`Microsoft.WSL.Containers` package is itself `net8.0-windows10.0.19041.0`, which a
`net10.0-windows10.0.19041.0` or `net11.0-windows10.0.19041.0` project consumes without issue.)

## What the package does for you

`Purview.Containers.Wsl` ships two `buildTransitive` files, imported by NuGet for **direct and
transitive** references:

| File | Effect |
| --- | --- |
| `…Wsl.props` | defaults `WindowsSdkPackageVersion` to `10.0.26100.80` when unset, and registers the `wsl` backend |
| `…Wsl.targets` | **Windows consumer:** defaults `PlatformTarget` to `x64` when unset or `AnyCPU` and no `RuntimeIdentifier` is selected, and fails with `PCC0001`/`PCC0002` when the target framework or platform cannot be supported. **Platform-neutral consumer:** copies the Windows implementation payload — implementation, WSLC projection, its Windows SDK dependencies, the native SDK — into `wslc/` beside the output on a Windows build host, so the facade can load it at run time. |

An explicit `PlatformTarget`, `RuntimeIdentifier` or `WindowsSdkPackageVersion` in the consumer
always wins.

A **service module** is backend-neutral and brings no backend. Reference the module *and* a backend
package, and use `PURVIEW_CONTAINERS_BACKEND` to pin which one runs.

> Because `buildTransitive` assets are framework-agnostic, NuGet no longer reports `NU1202` for an
> incompatible target framework: restore succeeds and the **guard target** produces the failure
> instead. That is deliberate. `PCC0001` names the required target framework, whereas an empty
> compile-asset set only produces `CS0246` errors in your own code — which blame the wrong thing.

## Error reference

| Error | Raised by | Means | Fix |
| --- | --- | --- | --- |
| `PCC0001` | the shipped guard target | the target framework is neither .NET 10 nor .NET 11 on Windows `10.0.19041.0`+ nor a platform-neutral .NET 10/.NET 11 framework | retarget as above |
| `PCC0002` | the shipped guard target | the consumer is not x64 (ARM64 and x86 are not supported) | remove `PlatformTarget` (x64 is the default), set it to `x64`, or choose the `win-x64` `RuntimeIdentifier` |
| `CS1705` naming `Microsoft.Windows.SDK.NET` | the compiler | the resolved Windows SDK targeting pack is older than the projection's | pin `WindowsSdkPackageVersion` to `10.0.26100.80` |
| `CS0234`/`CS0246` for `Microsoft.WSL`/`Purview` types | the compiler | the package contributed no compile assets — an unsupported target framework, or an unverified escape hatch | fix the target framework; `PCC0001` says the same thing more clearly |
| `NETSDK1100` | the SDK | building a Windows-targeted project on a non-Windows agent | set `EnableWindowsTargeting=true` |

### `EnableWindowsTargeting` on a non-Windows agent

It lets a Linux build agent *compile* a Windows-targeted project. It does **not** make the
implementation runnable there — the facade still reports `wsl` unavailable, and Docker is selected.
Use it to keep one solution building in CI, not as a way to run WSLC off Windows.

## Verifying all of this

The repository proves every row above rather than asserting it:

- `Wsl.UnitTests/ConsumerRequirementsTests.cs` guards the **shape** inside the normal unit run — the
  Windows build targets `.NETCoreApp,Version=v11.0`, targets `Windows10.0.19041.0`, and declares only
  that OS platform. `Wsl.UnitTests/BuildFeatureTests.cs` guards that the net11.0 build has no
  compiler-generated async state machines (runtime-async is on). If either drifts, the test fails, and
  this page, the package READMEs and the `buildTransitive` defaults all have to move with it.
- `just verify-consumers` proves the **behaviour**: it packs, then builds throwaway consumer projects
  for every documented outcome, including the guard errors (`PlatformTarget=x86`, `PlatformTarget=arm64`
  and `RuntimeIdentifier=win-arm64` → `PCC0002`;
  `net8.0-windows` → `PCC0001`; `net11.0-windows` without an OS version → `PCC0001`). It sits outside
  the `[Category=Unit]` filter because it restores from nuget.org.

That split is the point: **a library's own build is not a consumer**, so a claim about consumers is
only true if something builds one.

## Related skills

`purview-containers-quickstart` for packages and backend selection, and
`purview-containers-testing` for the test tiers and CI shape.
