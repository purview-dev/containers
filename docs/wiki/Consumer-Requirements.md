# Consumer Requirements

> **Experimental.** `Purview.Containers.*` is an experimental project: the public API, the
> defaults and these requirements can change between prereleases, and there is no production
> support guarantee. Treat every version as a preview and pin the exact package version you build
> against.

The WSL Containers backend package is **multi-target**. It ships a Windows build
(`net10.0-windows10.0.19041.0`, the implementation, built on the `Microsoft.WSL.Containers`
projection) and a platform-neutral build (`net10.0`, a facade). A Windows-targeting project binds the
implementation directly; any other project binds the facade, which builds on **every** platform and, on
a Windows host, loads the implementation at run time. That is what lets a `net10.0` test project that
references the WSL backend (or the umbrella `Purview.Containers`, which also brings Docker) run on WSLC on
a developer's Windows machine and on Docker in a Linux CI job with no configuration change. This page is
the authoritative statement of that contract, of the workarounds
that exist for it, and of how each of them is verified.

## Which package requires what

| Package | Target framework | Notes |
| --- | --- | --- |
| `Purview.Containers` | `net10.0`, any platform | The umbrella: brings `Purview.Containers.Core` plus both backends. One reference; no target-framework requirement beyond .NET 10. |
| `Purview.Containers.Core` | `net10.0`, any platform | Backend-neutral abstractions (namespace `Purview.Containers`). |
| `Purview.Containers.<Module>` | `net10.0`, any platform | Service modules. Restore anywhere; needs a backend package to actually run. |
| `Purview.Containers.Wsl` | `net10.0` and `net10.0-windows10.0.19041.0` | The WSL Containers backend: a portable facade plus the Windows implementation. **The requirements on this page are its contract.** |
| `Purview.Containers.Docker` | `net10.0`, any platform | The Docker backend (Testcontainers). Needs a reachable Docker daemon, not a Windows target framework. |

Everything below describes the **WSL Containers backend**. A consumer of a different backend (for
example `Purview.Containers.Docker`) needs only a portable `net10.0` project: the
abstractions and the service modules are portable `net10.0` assets. For how to use and switch between the
backends, see [Backends: WSLC or Docker](Backends.md).

## Copy this into your project

Most projects need **nothing at all**: a `net10.0` project binds the facade and gets automatic backend
selection. A Windows-targeting project can be explicit:

```xml
<PropertyGroup>
	<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
	<PlatformTarget>x64</PlatformTarget> <!-- or arm64 -->
	<WindowsSdkPackageVersion>10.0.26100.80</WindowsSdkPackageVersion>
</PropertyGroup>
```

The last two lines are optional for most projects: the packages ship MSBuild defaults that supply
them (see [What the packages do for you](#what-the-packages-do-for-you)). They are shown here
because being explicit makes the requirements visible to the next person who reads your project
file, and because the defaults only fill in a value when you have not chosen one.

## The requirements in detail

| # | Requirement | Why it exists |
| --- | --- | --- |
| 1 | **.NET 10 or later** | `Purview.Containers.Wsl` ships `lib/net10.0/` (facade) and `lib/net10.0-windows10.0.19041/` (implementation). `Purview.Containers` and the service modules ship `lib/net10.0/` too. |
| 2 | **A Windows-specific target framework that names the OS version** (`net10.0-windows10.0.19041.0` or later) — *only when you want the implementation bound directly; a platform-neutral `net10.0` project binds the facade and still runs WSLC on a Windows host* | The `Microsoft.WSL.Containers` projection only ships Windows assets. `net10.0-windows` on its own is not enough. |
| 3 | **64-bit** (`PlatformTarget` `x64`/`arm64`, or a matching `RuntimeIdentifier`) | The native `wslcsdk.dll` ships for `win-x64` and `win-arm64` only. |
| 4 | **`WindowsSdkPackageVersion` at least `10.0.26100.80`** | The projection is compiled against `Microsoft.Windows.SDK.NET` `10.0.26100.79`; the pack the SDK resolves for a `10.0.19041.0` target framework is older. |
| 5 | **A Windows 10/11 host with WSL Containers** to actually run containers | The library drives WSLC; it never installs or updates WSL for you. |

### 1. .NET 10 or later

`Purview.Containers.Wsl` ships a `net10.0` facade and a `net10.0-windows10.0.19041.0` implementation,
so it restores on any .NET 10+ project. The repository's own SDK is .NET 11 (`global.json` pins
`11.0.100-rc.1.26425.128`), but that is a build-time detail, not a consumer requirement.

### 2. A Windows-specific target framework (optional)

The **recommended** choice is a platform-neutral target framework. It binds the portable facade and
gives you the automatic "WSLC on a Windows machine, Docker everywhere else" behaviour:

```xml
<TargetFramework>net10.0</TargetFramework>                      <!-- portable facade; auto-selects WSLC or Docker -->
```

A Windows target framework binds the implementation directly (no run-time load):

```xml
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>  <!-- implementation; correct -->
```

```xml
<TargetFramework>net10.0-windows</TargetFramework>              <!-- fails: platform version defaults to 7.0 -->
```

`net10.0-windows` resolves its platform version to `7.0`, which is older than the `10.0.19041.0`
the implementation was built against, so NuGet selects no compile assets for it. Always spell out the
version. (The `Microsoft.WSL.Containers` package itself is `net8.0-windows10.0.19041.0`, which a
`net10.0-windows10.0.19041.0` project consumes without issue.)

### 3. 64-bit consumers only

The native WSL Containers SDK is shipped for `win-x64` and `win-arm64` only. A 32-bit (`x86`)
consumer builds but cannot load `wslcsdk.dll` at runtime, so the packages fail the build instead:

```text
error PCC0002: Purview.Containers.Wsl requires a 64-bit (x64 or arm64) consumer ...
```

### 4. Windows SDK targeting pack version

`Microsoft.WSL.Containers` 3.0.1's projection references `Microsoft.Windows.SDK.NET`
`10.0.26100.79`. Targeting a Windows target framework (`net10.0-windows10.0.19041.0`) resolves a lower pack
(`10.0.19041.x`) by default and the compiler rejects the mismatch:

```text
error CS1705: Assembly 'wslcsdkcs' ... uses 'Microsoft.Windows.SDK.NET' which has a higher
version than referenced assembly 'Microsoft.Windows.SDK.NET'
```

Pin `10.0.26100.80` (the nearest published version) as shown above. The packages set this default
for you, so you only need it if you want to override the value deliberately.

### 5. Host prerequisites

The build requirements above are host-independent. Running containers needs a Windows 10/11 host
with WSL Containers installed (`wsl --install --no-distribution`) — see
[Getting Started](Getting-Started.md).

## What the packages do for you

`Purview.Containers.Wsl` ships two `buildTransitive` MSBuild files. NuGet imports them for **direct and
transitive references**, so a project that references the backend package directly — or through another
project that does — gets them automatically.

> A **service module** (`Purview.Containers.PostgreSql`, `Redis`, `MsSql`, `RabbitMq`, `Azurite`, `Nats`,
> `MySql`, `AzureKeyVaultEmulator`) is backend-neutral and does **not** bring a backend, so reference the module **and**
> `Purview.Containers.Wsl` (or another backend package) to run it. Use `Purview.Containers.Docker` to run
> the same module on Docker, and `PURVIEW_CONTAINERS_BACKEND` to choose between them.

| Package path | Effect |
| --- | --- |
| `buildTransitive/Purview.Containers.Wsl.props` | Defaults `WindowsSdkPackageVersion` to `10.0.26100.80` when the consumer has not set it, and registers the `wsl` backend. |
| `buildTransitive/Purview.Containers.Wsl.targets` | For a **Windows** consumer: defaults `PlatformTarget` to `x64` when it is unset (or `AnyCPU`) and no `RuntimeIdentifier` is selected, and fails the build with `PCC0001`/`PCC0002` when the target framework or the platform cannot be supported. For a **platform-neutral** consumer: copies the Windows implementation payload (the implementation, the WSLC projection, its Windows SDK dependencies and the native SDK) into `wslc/` next to the output on a Windows build host, so the facade can load it at run time. |

A consumer therefore only has to choose a target framework — and the portable one is best, because it
runs on WSLC locally and Docker in CI with no further configuration:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework> <!-- auto: WSLC on Windows, Docker elsewhere -->
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Purview.Containers.PostgreSql" Version="1.0.0-prerelease.1" />
		<PackageReference Include="Purview.Containers.Wsl" Version="1.0.0-prerelease.1" />
	</ItemGroup>
</Project>
```

An explicit `PlatformTarget`, `RuntimeIdentifier` or `WindowsSdkPackageVersion` in the consumer
always wins over these defaults.

> Because `buildTransitive` assets are framework-agnostic, NuGet no longer reports `NU1202` for an
> incompatible target framework: restore succeeds and the failure comes from the guard targets
> instead. That is deliberate — `PCC0001` names the required target framework, whereas an empty
> compile-asset set only produces `CS0246` errors in your own code.

## Error reference

| Error | Raised by | Meaning | Fix |
| --- | --- | --- | --- |
| `PCC0001` | the shipped guard target | The consumer's target framework is neither .NET 10+ on Windows 10.0.19041.0+ nor a platform-neutral .NET 10+ framework | Retarget as above. |
| `PCC0002` | the shipped guard target | The consumer is not 64-bit | Remove your `PlatformTarget`, set it to `x64`/`arm64`, or choose a matching `RuntimeIdentifier`. |
| `CS1705` | the C# compiler | `WindowsSdkPackageVersion` is older than the projection requires | Set `WindowsSdkPackageVersion` to `10.0.26100.80` or later. |
| `CS8012` | the C# compiler | A referenced assembly targets a different processor (seen when a consumer forces `x86`) | Use a 64-bit consumer. |
| `NETSDK1100` | the .NET SDK | A Windows-targeted project is being built on a non-Windows operating system | Set `EnableWindowsTargeting` (see below). |
| `CS0234`/`CS0246` for `Microsoft.WSL`/`Purview` types | the C# compiler | The package contributed no compile assets (an unsupported target framework, or an unverified escape hatch) | Fix the target framework; `PCC0001` explains the same problem with a clearer message. |

## Workaround: building on a non-Windows CI agent

`net*-windows…` projects can be restored, compiled and unit-tested on Linux or macOS, provided
the build opts in:

```xml
<PropertyGroup>
	<EnableWindowsTargeting>true</EnableWindowsTargeting>
</PropertyGroup>
```

Without it the SDK fails with `NETSDK1100` (*"To build a project targeting Windows on this operating
system, set the EnableWindowsTargeting property to true"*).

This is exactly how this repository's own CI works: the shared `purview-dev/build` workflow runs on
**`ubuntu-latest`**, so `src/Directory.Build.props` sets `EnableWindowsTargeting=true` and the
pipeline is filtered to `[Category=Unit]`. See [Testing](Testing.md).

What it does and does not do:

- ✅ restores the Windows targeting packs and compiles `net*-windows` assemblies;
- ✅ runs unit tests that never touch the WSLC runtime;
- ❌ does not provide WSL Containers — the WSLC integration suites need a Windows host;
- ❌ does not make `wslcsdk.dll` loadable; anything that opens a session must run on Windows.

## Workaround: consuming from a project older than .NET 10

**There is none.** A project that targets Windows but not .NET 10 (for example
`net8.0-windows10.0.19041.0`) cannot use these packages, and the repository verifies that no
escape hatch exists.

The historical suggestion is `AssetTargetFallback`, which tells NuGet to consider an additional
target framework for the project's package references:

```xml
<PropertyGroup>
	<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
	<AssetTargetFallback>net10.0-windows10.0.19041.0</AssetTargetFallback>
</PropertyGroup>
```

It does **not** work here. `AssetTargetFallback` is not applied to `netcoreapp`-family packages, so
the package still contributes no compile assets and the build fails in your own code with
`CS0234`/`CS0246`. `PCC0001` fires as well.

Even if it did compile, it would not help: the `lib/` assemblies target .NET 10, so they can only be
loaded by a .NET 10+ runtime. A consumer would have to move to .NET 10 to run them anyway.

If you need this library from a project you cannot retarget, keep the container work in a small
**.NET 10+ test project** (which can reference the older project) rather than trying to
consume the packages from the older project.

## Multi-targeting

Multi-targeting is **no longer required** for the WSL Containers backend: a platform-neutral `net10.0`
target binds the portable facade. If you multi-target for other reasons, reference the package
unconditionally — both inner builds are supported:

```xml
<PropertyGroup>
	<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>
</PropertyGroup>
<ItemGroup>
	<PackageReference Include="Purview.Containers.Wsl" Version="1.0.0-prerelease.1" />
</ItemGroup>
```

The Windows inner build binds the implementation; the portable inner build binds the facade. The
historical pattern — multi-targeting with the reference only on the Windows inner build — still works,
because the package keeps a `net10.0-windows10.0.19041` build, but the condition is no longer needed.

## Verifying these requirements

`just verify-consumers` packs the solution and builds twenty-one throwaway consumer projects against the
produced packages, asserting every claim on this page:

| Case | Consumer | Expected outcome |
| --- | --- | --- |
| 01 | `.NET 11` + Windows with the documented settings | builds; `wslcsdk.dll` copied to the output |
| 02 | …with a stale `WindowsSdkPackageVersion` | `CS1705` |
| 03 | …with `PlatformTarget=x86` | `PCC0002` |
| 04 | …with no settings at all | builds via the shipped `buildTransitive` defaults |
| 05 | a module package plus the WSL Containers backend, with no settings | builds; `wslcsdk.dll` copied (the defaults are transitive through the backend) |
| 06 | `net8.0-windows` + `AssetTargetFallback` | `PCC0001` — the escape hatch does not work |
| 07 | `net8.0-windows` | `PCC0001` |
| 08 | plain `net11.0` (platform-neutral facade) | builds; the `wsl` registration is generated |
| 09 | …with `PlatformTarget=AnyCPU` | builds (corrected to `x64`) |
| 10 | the `net11.0-windows` shorthand (no OS version) | `PCC0001` |
| 11 | multi-targeting with a conditional `PackageReference` | builds |
| 12 | multi-targeting with an unconditional `PackageReference` | builds (both inner builds are supported) |
| 13 | a `net10.0` consumer of the portable abstractions | builds |
| 14 | a `net10.0` consumer of the Docker backend | builds; the backend registration is generated into the consumer |
| 15 | a `net10.0` consumer of a service module, with no backend package | builds |
| 16 | a `.NET 11` Windows consumer with **both** backends referenced | builds; both registrations are generated |
| 17 | the documented backend example on WSL Containers | builds |
| 18 | the documented backend example on Docker (`net10.0`) | builds |
| 19 | a plain `net10.0` consumer of the WSL Containers backend | builds; the `wsl` registration is generated (the portable facade) |
| 20 | a `net10.0` consumer of two modules with **both** backends (the auto shape) | builds; both registrations are generated |
| 21 | a `net10.0` consumer of modules with only the umbrella `Purview.Containers` | builds; both registrations are generated (one reference) |

The script is `scripts/verify-consumers.ps1` and it only writes to the temp folder. It needs network
access (it restores transitive dependencies from nuget.org), so it is a local/CI-explicit step
rather than part of the `[Category=Unit]` filter.

## Related

- [Getting Started](Getting-Started.md) — prerequisites and the first container.
- [Packaging](Packaging.md) — what each package contains, including the `buildTransitive` assets.
- [Testing](Testing.md) — the Linux CI run, the unit filter and the serial WSLC rule.
- [Modules](Modules.md) — the service modules that inherit these defaults.


