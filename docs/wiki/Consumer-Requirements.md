# Consumer Requirements

> **Experimental.** `Purview.WslContainers.*` is an experimental project: the public API, the
> defaults and these requirements can change between prereleases, and there is no production
> support guarantee. Treat every version as a preview and pin the exact package version you build
> against.

Every package targets `net11.0-windows10.0.19041.0` and is built on the
`Microsoft.WSL.Containers` WinRT projection, so a consuming project **must be a .NET 11 (or later)
project that targets Windows specifically**. This page is the authoritative statement of that
contract, of the workarounds that exist for it, and of how each of them is verified.

## Copy this into your project

```xml
<PropertyGroup>
	<TargetFramework>net11.0-windows10.0.19041.0</TargetFramework>
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
| 1 | **.NET 11 or later** | Every `lib/` asset in every package is built for `net11.0-windows10.0.19041.0`. |
| 2 | **A Windows-specific target framework that names the OS version** (`net11.0-windows10.0.19041.0` or later) | The `Microsoft.WSL.Containers` projection only ships Windows assets. `net11.0-windows` on its own is not enough. |
| 3 | **64-bit** (`PlatformTarget` `x64`/`arm64`, or a matching `RuntimeIdentifier`) | The native `wslcsdk.dll` ships for `win-x64` and `win-arm64` only. |
| 4 | **`WindowsSdkPackageVersion` at least `10.0.26100.80`** | The projection is compiled against `Microsoft.Windows.SDK.NET` `10.0.26100.79`; the pack the SDK resolves for a `10.0.19041.0` target framework is older. |
| 5 | **A Windows 10/11 host with WSL Containers** to actually run containers | The library drives WSLC; it never installs or updates WSL for you. |

### 1. .NET 11 or later

The repository, the packages and the tests all target .NET 11 (`global.json` pins
`11.0.100-rc.1.26425.128`). A `net10.0-windows…` or `net8.0-windows…` project cannot consume the
packages; see [Non-.NET-11 Windows projects](#workaround-consuming-from-a-non-net-11-windows-project).

### 2. A Windows-specific target framework

```xml
<TargetFramework>net11.0-windows10.0.19041.0</TargetFramework>   <!-- correct -->
```

```xml
<TargetFramework>net11.0-windows</TargetFramework>               <!-- fails: platform version defaults to 7.0 -->
<TargetFramework>net11.0</TargetFramework>                       <!-- fails: not a Windows TFM -->
```

`net11.0-windows` resolves its platform version to `7.0`, which is older than the `10.0.19041.0`
the packages were built against, so NuGet selects no compile assets for it. Always spell out the
version. (The `Microsoft.WSL.Containers` package itself is `net8.0-windows10.0.19041.0`, which a
`net11.0-windows10.0.19041.0` project consumes without issue.)

### 3. 64-bit consumers only

The native WSL Containers SDK is shipped for `win-x64` and `win-arm64` only. A 32-bit (`x86`)
consumer builds but cannot load `wslcsdk.dll` at runtime, so the packages fail the build instead:

```text
error PWC0002: Purview.WslContainers requires a 64-bit (x64 or arm64) consumer ...
```

### 4. Windows SDK targeting pack version

`Microsoft.WSL.Containers` 3.0.1's projection references `Microsoft.Windows.SDK.NET`
`10.0.26100.79`. Targeting `net11.0-windows10.0.19041.0` resolves a lower pack
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

`Purview.WslContainers` ships two `buildTransitive` MSBuild files. NuGet imports them for **direct
references and transitive references**, so every module package
(`Purview.WslContainers.PostgreSql`, `Redis`, `MsSql`, `RabbitMq`, `Azurite`, `Nats`, `MySql`) gets
them too through its dependency on the core package.

| Package path | Effect |
| --- | --- |
| `buildTransitive/Purview.WslContainers.props` | Defaults `WindowsSdkPackageVersion` to `10.0.26100.80` when the consumer has not set it. |
| `buildTransitive/Purview.WslContainers.targets` | Defaults `PlatformTarget` to `x64` when it is unset (or `AnyCPU`) and no `RuntimeIdentifier` is selected, and fails the build with `PWC0001`/`PWC0002` when the target framework or the platform cannot be supported. |

A consumer therefore only has to choose a target framework:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net11.0-windows10.0.19041.0</TargetFramework>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Purview.WslContainers.PostgreSql" Version="1.0.0-prerelease.1" />
	</ItemGroup>
</Project>
```

An explicit `PlatformTarget`, `RuntimeIdentifier` or `WindowsSdkPackageVersion` in the consumer
always wins over these defaults.

> Because `buildTransitive` assets are framework-agnostic, NuGet no longer reports `NU1202` for an
> incompatible target framework: restore succeeds and the failure comes from the guard targets
> instead. That is deliberate — `PWC0001` names the required target framework, whereas an empty
> compile-asset set only produces `CS0246` errors in your own code.

## Error reference

| Error | Raised by | Meaning | Fix |
| --- | --- | --- | --- |
| `PWC0001` | the shipped guard target | The consumer's target framework is not .NET 11+ targeting Windows 10.0.19041.0+ | Retarget as above, or make the reference conditional when multi-targeting. |
| `PWC0002` | the shipped guard target | The consumer is not 64-bit | Remove your `PlatformTarget`, set it to `x64`/`arm64`, or choose a matching `RuntimeIdentifier`. |
| `CS1705` | the C# compiler | `WindowsSdkPackageVersion` is older than the projection requires | Set `WindowsSdkPackageVersion` to `10.0.26100.80` or later. |
| `CS8012` | the C# compiler | A referenced assembly targets a different processor (seen when a consumer forces `x86`) | Use a 64-bit consumer. |
| `NETSDK1100` | the .NET SDK | A Windows-targeted project is being built on a non-Windows operating system | Set `EnableWindowsTargeting` (see below). |
| `CS0234`/`CS0246` for `Microsoft.WSL`/`Purview` types | the C# compiler | The package contributed no compile assets (an unsupported target framework, or an unverified escape hatch) | Fix the target framework; `PWC0001` explains the same problem with a clearer message. |

## Workaround: building on a non-Windows CI agent

`net11.0-windows…` projects can be restored, compiled and unit-tested on Linux or macOS, provided
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

## Workaround: consuming from a non-.NET-11 Windows project

**There is none.** A project that targets Windows but not .NET 11 (for example
`net8.0-windows10.0.19041.0`) cannot use these packages, and the repository verifies that no
escape hatch exists.

The historical suggestion is `AssetTargetFallback`, which tells NuGet to consider an additional
target framework for the project's package references:

```xml
<PropertyGroup>
	<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
	<AssetTargetFallback>net11.0-windows10.0.19041.0</AssetTargetFallback>
</PropertyGroup>
```

It does **not** work here. `AssetTargetFallback` is not applied to `netcoreapp`-family packages, so
the package still contributes no compile assets and the build fails in your own code with
`CS0234`/`CS0246`. `PWC0001` fires as well.

Even if it did compile, it would not help: the `lib/` assemblies are .NET 11 assemblies, so they can
only be loaded by a .NET 11 runtime. A consumer would have to move to .NET 11 to run them anyway.

If you need this library from a project you cannot retarget, keep the container work in a small
**.NET 11 Windows test project** (which can reference the older project) rather than trying to
consume the packages from the older project.

## Multi-targeting

Only the Windows inner build may reference the packages, so make the reference conditional:

```xml
<PropertyGroup>
	<TargetFrameworks>net11.0;net11.0-windows10.0.19041.0</TargetFrameworks>
</PropertyGroup>
<ItemGroup Condition="'$(TargetFramework)' == 'net11.0-windows10.0.19041.0'">
	<PackageReference Include="Purview.WslContainers" Version="1.0.0-prerelease.1" />
</ItemGroup>
```

An unconditional reference fails the whole build: the `net11.0` inner build reports `PWC0001`.
Keep any code that uses the library behind the same condition —
`#if WINDOWS` (defined by the SDK for Windows target frameworks) or a conditional `Compile` item —
so the non-Windows inner build can still compile.

## Verifying these requirements

`just verify-consumers` packs the solution and builds twelve throwaway consumer projects against the
produced packages, asserting every claim on this page:

| Case | Consumer | Expected outcome |
| --- | --- | --- |
| 01 | `.NET 11` + Windows with the documented settings | builds; `wslcsdk.dll` copied to the output |
| 02 | …with a stale `WindowsSdkPackageVersion` | `CS1705` |
| 03 | …with `PlatformTarget=x86` | `PWC0002` |
| 04 | …with no settings at all | builds via the shipped `buildTransitive` defaults |
| 05 | a module package with no settings | builds (the defaults are transitive) |
| 06 | `net8.0-windows` + `AssetTargetFallback` | `PWC0001` — the escape hatch does not work |
| 07 | `net8.0-windows` | `PWC0001` |
| 08 | plain `net11.0` | `PWC0001` |
| 09 | …with `PlatformTarget=AnyCPU` | builds (corrected to `x64`) |
| 10 | the `net11.0-windows` shorthand (no OS version) | `PWC0001` |
| 11 | multi-targeting with a conditional `PackageReference` | builds |
| 12 | multi-targeting with an unconditional `PackageReference` | `PWC0001` |

The script is `scripts/verify-consumers.ps1` and it only writes to the temp folder. It needs network
access (it restores transitive dependencies from nuget.org), so it is a local/CI-explicit step
rather than part of the `[Category=Unit]` filter.

## Related

- [Getting Started](Getting-Started.md) — prerequisites and the first container.
- [Packaging](Packaging.md) — what each package contains, including the `buildTransitive` assets.
- [Testing](Testing.md) — the Linux CI run, the unit filter and the serial WSLC rule.
- [Modules](Modules.md) — the service modules that inherit these defaults.


