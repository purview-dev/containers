#Requires -Version 7.0
<#
.SYNOPSIS
	Verifies the consumer requirements and workarounds documented in
	docs/wiki/Consumer-Requirements.md against the packages produced by `just pack`.

.DESCRIPTION
	Generates throwaway consumer projects in the temp folder, builds each one against the
	packages in ./artifacts, and asserts the outcome the documentation promises. Nothing is
	written inside the repository, so the check is safe to run locally; it is deliberately
	not part of the `[Category=Unit]` pipeline run because it packs and restores from
	nuget.org.

	Expectations and their documentation:
	  01  .NET 11 + Windows with the documented settings              -> builds, wslcsdk.dll copied
	  02  ...with a stale WindowsSdkPackageVersion                    -> CS1705
	  03  ...with PlatformTarget=x86                                  -> fails PCC0002
	  04  ...with no settings at all (buildTransitive defaults)       -> builds, wslcsdk.dll copied
	  05  a module package with no settings (defaults are transitive)  -> builds
	  06  net8.0-windows + AssetTargetFallback escape hatch           -> PCC0001 (the hatch does not work)
	  07  net8.0-windows without the escape hatch                     -> PCC0001
	  08  plain net11.0 (platform-neutral facade)                     -> builds, wsl registration generated
	  09  ...with PlatformTarget=AnyCPU (corrected to x64)            -> builds, wslcsdk.dll copied
	  10  the net11.0-windows shorthand TFM (no OS version)           -> PCC0001
	  11  multi-targeting with a conditional PackageReference          -> builds
	  12  multi-targeting with an unconditional PackageReference       -> builds (both inner builds supported)
	  13  net10.0 + core abstractions                                 -> builds
	  14  net10.0 + Docker backend                                    -> builds, backend registration generated
	  15  net10.0 + service module (no backend package)               -> builds
	  16  net11.0 windows + both backends (auto detection)            -> builds, both registrations generated
	  17  the documented backend example, on WSL Containers          -> builds
	  18  the documented backend example, on Docker (net10.0)         -> builds
	  19  plain net10.0 + WSL backend (portable facade, auto)         -> builds, wsl registration generated
	  20  net10.0 + Redis/PostgreSql + both backends (auto)           -> builds, both registrations generated
	  21  net10.0 + umbrella Purview.Containers + modules (one ref)   -> builds, both registrations generated

.PARAMETER FeedPath
	Folder holding the packed .nupkg files. Defaults to <repo>/artifacts.

.PARAMETER PackageVersion
	Package version to consume. Defaults to the version in package.json.

.PARAMETER WorkPath
	Scratch folder for the generated consumers. Defaults to
	%TEMP%/wslc-consumer-verification.

.PARAMETER Only
	Runs only the cases with these ids, e.g. -Only 13,14. Defaults to every case.

.PARAMETER Keep
	Keep the generated consumer projects for inspection instead of deleting them.

.EXAMPLE
	just verify-consumers

.EXAMPLE
	just verify-consumers -Keep
#>
[CmdletBinding()]
param(
	[string] $FeedPath,
	[string] $PackageVersion,
	[string] $WorkPath = (Join-Path ([System.IO.Path]::GetTempPath()) 'wslc-consumer-verification'),
	[string[]] $Only = @(),
	[switch] $Keep
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$FeedPath = if ($FeedPath) { (Resolve-Path $FeedPath).Path } else { Join-Path $repoRoot 'artifacts' }

if (-not $PackageVersion) {
	$packageJson = Get-Content (Join-Path $repoRoot 'package.json') -Raw | ConvertFrom-Json
	$PackageVersion = $packageJson.version
}

if (-not (Test-Path $FeedPath)) {
	throw "Package feed '$FeedPath' does not exist. Run 'just pack' first."
}

$corePackage = Join-Path $FeedPath "Purview.Containers.Wsl.$PackageVersion.nupkg"
if (-not (Test-Path $corePackage)) {
	throw "Package '$corePackage' was not found. Run 'just pack' (or pass -FeedPath) first."
}

$globalPackagesLine = & dotnet nuget locals global-packages --list | Select-Object -First 1
$globalPackages = ($globalPackagesLine -replace '^global-packages:\s*', '').Trim()

# The packages keep the same version between local packs, so a stale extraction in the global
# packages folder would silently test the previous build. Drop the extracted version of every
# Purview.Containers package before consuming the feed.
foreach ($packageFolder in @(Get-ChildItem -Path $globalPackages -Directory -Filter 'purview.containers*' -ErrorAction SilentlyContinue)) {
	$versioned = Join-Path $packageFolder.FullName $PackageVersion

	if (Test-Path $versioned) {
		Remove-Item $versioned -Recurse -Force
	}
}

# MSBuild reuses build nodes between invocations and can keep serving the parse of a package file
# that was replaced at the same path (the version never changes between local packs). Shut the
# servers down so the freshly packed assets are parsed.
& dotnet build-server shutdown | Out-Null

if (Test-Path $WorkPath) {
	Remove-Item $WorkPath -Recurse -Force
}

New-Item -ItemType Directory -Path $WorkPath -Force | Out-Null

# Local feed first, then nuget.org for the transitive dependencies
# (Microsoft.WSL.Containers, Npgsql, ...).
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<clear />
		<add key="local-artifacts" value="$FeedPath" />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
	</packageSources>
</configuration>
"@
Set-Content -Path (Join-Path $WorkPath 'nuget.config') -Value $nugetConfig

# A consumer that touches a Microsoft.WSL.Containers type is the realistic shape: the
# C#/WinRT projection is what pulls in the Windows SDK version that CS1705 complains about.
# OutputType=Exe so the runtime assets (wslcsdk.dll) are copied to the output for inspection.
$apiSource = @'
using Microsoft.WSL.Containers;
using Purview.Containers;
using Purview.Containers.Wsl;

namespace Consumer;

public static class Program
{
	public static SessionSettings Session() => new("consumer-session", @"C:\temp\wslc");

	public static IContainer Container() =>
		new ContainerBuilder().WithImage("docker.io/library/alpine:3.19").Build();

	public static void Main() { }
}
'@

$portableSource = @'
namespace Consumer;

public static class Program
{
	public static string Hello() => "hello";

	public static void Main() { }
}
'@

# Single-quoted template: {{...}} tokens are replaced so MSBuild's own $(...) syntax is
# never touched by PowerShell interpolation.
$projectTemplate = @'
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		{{FRAMEWORK}}
		<OutputType>Exe</OutputType>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
		{{PROPERTIES}}
	</PropertyGroup>
{{ITEMS}}
	<ItemGroup>
		<PackageReference Include="{{PACKAGE}}" Version="{{VERSION}}" {{REFERENCE_ATTRIBUTES}} />
	</ItemGroup>
</Project>
'@

$net11Windows = 'net11.0-windows10.0.19041.0'
$net8Windows = 'net8.0-windows10.0.19041.0'
$sdkPackage = 'Purview.Containers.Wsl'
$modulePackage = 'Purview.Containers.PostgreSql'
$sdkVersion = '<WindowsSdkPackageVersion>10.0.26100.80</WindowsSdkPackageVersion>'
$staleSdkVersion = '<WindowsSdkPackageVersion>10.0.19041.38</WindowsSdkPackageVersion>'
$x64 = '<PlatformTarget>x64</PlatformTarget>'
$escapeHatch = '<AssetTargetFallback>net11.0-windows10.0.19041.0</AssetTargetFallback>'

$multiTargetItems = @'
	<ItemGroup Condition="'$(TargetFramework)' == 'net11.0-windows10.0.19041.0'">
		<Compile Include="Smoke.Windows.cs" />
	</ItemGroup>
	<ItemGroup Condition="'$(TargetFramework)' != 'net11.0-windows10.0.19041.0'">
		<Compile Include="Smoke.Portable.cs" />
	</ItemGroup>
'@

# A consumer that touches the Docker backend. Nothing registers the backend here: the generated module
# initializer arrives from the package's buildTransitive assets, which is what case 14 asserts.
$dockerSource = @'
using Purview.Containers;
using Purview.Containers.Docker;

namespace Consumer;

public static class Program
{
	public static async Task<string> BackendName() => (await ContainerBackends.ResolveAsync()).Name;

	public static void Main() { }
}
'@

# A consumer that only references a service module: no backend package, so it restores and builds anywhere.
$portableModuleSource = @'
using Purview.Containers.PostgreSql;

namespace Consumer;

public static class Program
{
	public static PostgreSqlBuilder Builder() => new PostgreSqlBuilder().WithDatabase("tests");

	public static void Main() { }
}
'@

# A second backend package alongside the first: the developer-machine shape (WSLC plus Docker present, so
# automatic detection picks WSLC and the environment can pin Docker).
$bothBackendsItems = @'
	<ItemGroup>
		<PackageReference Include="Purview.Containers.Docker" Version="{{VERSION}}" />
	</ItemGroup>
'@

# The umbrella shape: one backend reference (Purview.Containers, which brings Core + WSL + Docker) plus the
# service modules. This is the "one reference" story documented in docs/wiki/Using-in-Your-Tests.md.
$umbrellaItems = @'
	<ItemGroup>
		<PackageReference Include="Purview.Containers.Redis" Version="{{VERSION}}" />
		<PackageReference Include="Purview.Containers.PostgreSql" Version="{{VERSION}}" />
	</ItemGroup>
'@

# The realistic Windows consumer shape: a service module plus the WSL Containers backend. The module is
# backend-neutral, so the backend package is what supplies the WSL build defaults and the guards.
$wslBackendItems = @'
	<ItemGroup>
		<PackageReference Include="Purview.Containers.Wsl" Version="{{VERSION}}" />
	</ItemGroup>
'@

# The "auto" shape documented in docs/wiki/Using-in-Your-Tests.md: a platform-neutral consumer with two
# service modules and BOTH backends, so the one project runs on WSLC (Windows) and Docker (everywhere else).
$autoItems = @'
	<ItemGroup>
		<PackageReference Include="Purview.Containers.Redis" Version="{{VERSION}}" />
		<PackageReference Include="Purview.Containers.Wsl" Version="{{VERSION}}" />
		<PackageReference Include="Purview.Containers.Docker" Version="{{VERSION}}" />
	</ItemGroup>
'@

$autoSource = @'
using Purview.Containers;
using Purview.Containers.PostgreSql;
using Purview.Containers.Redis;

namespace Consumer;

public static class Program
{
	public static async Task<string> SelectedBackendAsync() => (await ContainerBackends.ResolveAsync()).Name;

	public static RedisBuilder Cache() => new RedisBuilder();

	public static PostgreSqlBuilder Database() => new PostgreSqlBuilder().WithDatabase("app");

	public static void Main() { }
}
'@

# Mirrors the container code in the "The same test, either backend" example of docs/wiki/Backends.md, so
# the documented example is compiled against both backend packages on every run. The test assertion is
# omitted because the generated consumer project has no test framework.
$documentedBackendSource = @'
using Purview.Containers;
using Purview.Containers.Waiting;

namespace Consumer;

public static class Program
{
	public static async Task<ushort> MappedPortAsync()
	{
		await using var container = new ContainerBuilder()
			.WithImage("redis:7")
			.WithPortBinding(6379, assignRandomHostPort: true)
			.WithWaitStrategy(Wait.ForTcpPort(6379))
			.Build();

		await container.StartAsync();

		return container.GetMappedPublicPort(6379);
	}

	public static void Main() { }
}
'@

function New-ConsumerCase {
	param(
		[string] $Id,
		[string] $Name,
		[string] $Framework = "<TargetFramework>$net11Windows</TargetFramework>",
		[string] $Package = $sdkPackage,
		[string] $Properties = '',
		[string] $ReferenceAttributes = '',
		[string] $Items = '',
		[hashtable] $Sources = @{ 'Smoke.cs' = $apiSource },
		[string] $Expect = 'Builds',
		[string] $RequireFile = '',
		[string[]] $RequireText = @()
	)

	[pscustomobject]@{
		Id = $Id
		Name = $Name
		Framework = $Framework
		Package = $Package
		Properties = $Properties
		ReferenceAttributes = $ReferenceAttributes
		Items = $Items
		Sources = $Sources
		Expect = $Expect
		RequireFile = $RequireFile
		RequireText = $RequireText
	}
}

$cases = @(
	New-ConsumerCase -Id '01' -Name 'net11 windows + documented settings' `
		-Properties "$sdkVersion$x64" -RequireFile 'wslcsdk.dll'

	New-ConsumerCase -Id '02' -Name 'net11 windows + stale WindowsSdkPackageVersion' `
		-Properties "$staleSdkVersion$x64" -Expect 'CS1705'

	New-ConsumerCase -Id '03' -Name 'net11 windows + PlatformTarget=x86' `
		-Properties "$sdkVersion<PlatformTarget>x86</PlatformTarget>" -Expect 'PCC0002'

	New-ConsumerCase -Id '04' -Name 'net11 windows + buildTransitive defaults only' `
		-RequireFile 'wslcsdk.dll'

	New-ConsumerCase -Id '05' -Name 'module package + WSL backend (defaults are transitive)' `
		-Package $modulePackage `
		-Items $wslBackendItems `
		-RequireFile 'wslcsdk.dll'

	New-ConsumerCase -Id '06' -Name 'net8 windows + AssetTargetFallback escape hatch' `
		-Framework "<TargetFramework>$net8Windows</TargetFramework>" `
		-Properties "$escapeHatch$sdkVersion$x64" -Expect 'PCC0001'

	New-ConsumerCase -Id '07' -Name 'net8 windows without the escape hatch' `
		-Framework "<TargetFramework>$net8Windows</TargetFramework>" `
		-Properties "$sdkVersion$x64" -Expect 'PCC0001'

	New-ConsumerCase -Id '08' -Name 'plain net11.0 (platform-neutral facade)' `
		-Framework '<TargetFramework>net11.0</TargetFramework>' `
		-Sources @{ 'Smoke.Portable.cs' = $portableSource } `
		-RequireText 'WslContainerBackend.Create()'

	New-ConsumerCase -Id '09' -Name 'net11 windows + PlatformTarget=AnyCPU' `
		-Properties "$sdkVersion<PlatformTarget>AnyCPU</PlatformTarget>" -RequireFile 'wslcsdk.dll'

	New-ConsumerCase -Id '10' -Name 'net11.0-windows shorthand TFM (no OS version)' `
		-Framework '<TargetFramework>net11.0-windows</TargetFramework>' `
		-Properties "$sdkVersion$x64" -Expect 'PCC0001'

	New-ConsumerCase -Id '11' -Name 'multi-targeting + conditional PackageReference' `
		-Framework "<TargetFrameworks>net11.0;$net11Windows</TargetFrameworks>" `
		-Properties '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>' `
		-ReferenceAttributes "Condition=`"'`$(TargetFramework)' == '$net11Windows'`"" `
		-Items $multiTargetItems `
		-Sources @{ 'Smoke.Windows.cs' = $apiSource; 'Smoke.Portable.cs' = $portableSource }

	New-ConsumerCase -Id '12' -Name 'multi-targeting + unconditional PackageReference' `
		-Framework "<TargetFrameworks>net11.0;$net11Windows</TargetFrameworks>" `
		-Properties '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>' `
		-Items $multiTargetItems `
		-Sources @{ 'Smoke.Windows.cs' = $apiSource; 'Smoke.Portable.cs' = $portableSource }

	New-ConsumerCase -Id '13' -Name 'net10 + core abstractions' `
		-Framework '<TargetFramework>net10.0</TargetFramework>' `
		-Package 'Purview.Containers.Core' `
		-Sources @{ 'Smoke.Portable.cs' = $portableSource }

	New-ConsumerCase -Id '14' -Name 'net10 + docker backend (generated registration)' `
		-Framework '<TargetFramework>net10.0</TargetFramework>' `
		-Package 'Purview.Containers.Docker' `
		-RequireText 'DockerContainerBackend.Create()' `
		-Sources @{ 'Smoke.cs' = $dockerSource }

	New-ConsumerCase -Id '15' -Name 'net10 + service module (backend-neutral, no backend package)' `
		-Framework '<TargetFramework>net10.0</TargetFramework>' `
		-Package $modulePackage `
		-Sources @{ 'Smoke.cs' = $portableModuleSource }

	New-ConsumerCase -Id '16' -Name 'net11 windows + both backends (auto detection, WSLC first)' `
		-Package $sdkPackage `
		-Items $bothBackendsItems `
		-RequireText 'WslContainerBackend.Create()' `
		-Sources @{ 'Smoke.cs' = $dockerSource }

	New-ConsumerCase -Id '17' -Name 'documented backend example on WSL Containers' `
		-Package $sdkPackage `
		-Sources @{ 'Smoke.cs' = $documentedBackendSource }

	New-ConsumerCase -Id '18' -Name 'documented backend example on Docker (net10.0)' `
		-Framework '<TargetFramework>net10.0</TargetFramework>' `
		-Package 'Purview.Containers.Docker' `
		-Sources @{ 'Smoke.cs' = $documentedBackendSource }

	New-ConsumerCase -Id '19' -Name 'plain net10.0 + WSL backend (portable facade, auto)' `
		-Framework '<TargetFramework>net10.0</TargetFramework>' `
		-Sources @{ 'Smoke.Portable.cs' = $portableSource } `
		-RequireText 'WslContainerBackend.Create()'

	New-ConsumerCase -Id '20' -Name 'net10.0 + Redis/PostgreSql + both backends (auto, zero-config)' `
		-Framework '<TargetFramework>net10.0</TargetFramework>' `
		-Package $modulePackage `
		-Items $autoItems `
		-Sources @{ 'Smoke.Auto.cs' = $autoSource } `
		-RequireText @('WslContainerBackend.Create()', 'DockerContainerBackend.Create()')

	New-ConsumerCase -Id '21' -Name 'net10.0 + umbrella Purview.Containers + modules (one reference, auto)' `
		-Framework '<TargetFramework>net10.0</TargetFramework>' `
		-Package 'Purview.Containers' `
		-Items $umbrellaItems `
		-Sources @{ 'Smoke.Auto.cs' = $autoSource } `
		-RequireText @('WslContainerBackend.Create()', 'DockerContainerBackend.Create()')
)


Write-Host ''
Write-Host 'Purview.Containers.Wsl consumer verification' -ForegroundColor Cyan
Write-Host "  feed    : $FeedPath"
Write-Host "  version : $PackageVersion"
Write-Host "  scratch : $WorkPath"
Write-Host ''

$results = [System.Collections.Generic.List[object]]::new()

if ($Only.Count -gt 0) {
	# `pwsh -File script.ps1 -Only 13,14` binds the value as one comma-joined string, so split it. Ids are
	# zero-padded ('08'), so an unpadded value ('8') matches too.
	$wanted = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
	$cases = @(
		$cases | Where-Object {
			$id = $_.Id
			$wanted -contains $id -or ($id -match '^\d+$' -and $wanted -contains ([string][int]$id))
		}
	)

	if ($cases.Count -eq 0) {
		throw "No consumer cases matched -Only '$($wanted -join ', ')'."
	}
}

foreach ($case in $cases) {
	$slug = ($case.Name -replace '[^a-zA-Z0-9]+', '-').Trim('-').ToLowerInvariant()
	$directory = Join-Path $WorkPath ("{0}-{1}" -f $case.Id, $slug)
	New-Item -ItemType Directory -Path $directory -Force | Out-Null

	$project = $projectTemplate
	$project = $project.Replace('{{FRAMEWORK}}', $case.Framework)
	$project = $project.Replace('{{PROPERTIES}}', $case.Properties)
	$project = $project.Replace('{{ITEMS}}', $case.Items)
	$project = $project.Replace('{{PACKAGE}}', $case.Package)
	$project = $project.Replace('{{VERSION}}', $PackageVersion)
	$project = $project.Replace('{{REFERENCE_ATTRIBUTES}}', $case.ReferenceAttributes)
	Set-Content -Path (Join-Path $directory 'Consumer.csproj') -Value $project

	foreach ($source in $case.Sources.GetEnumerator()) {
		Set-Content -Path (Join-Path $directory $source.Key) -Value $source.Value
	}

	$output = & dotnet build (Join-Path $directory 'Consumer.csproj') --nologo -v minimal 2>&1 | Out-String
	$exitCode = $LASTEXITCODE

	$matched = $output -match [regex]::Escape($case.Expect)
	$passed = if ($case.Expect -eq 'Builds') { $exitCode -eq 0 } else { $exitCode -ne 0 -and $matched }

	$signal = if ($case.Expect -eq 'Builds') {
		"exit $exitCode"
	}
	elseif ($matched) {
		"exit $exitCode, matched $($case.Expect)"
	}
	else {
		"exit $exitCode, no $($case.Expect)"
	}

	if ($passed -and $case.RequireFile) {
		$found = @(Get-ChildItem -Path $directory -Recurse -Filter $case.RequireFile -File -ErrorAction SilentlyContinue)

		if ($found.Count -eq 0) {
			$passed = $false
			$signal = "$signal; $($case.RequireFile) not copied to the output"
		}
		else {
			$signal = "$signal; $($case.RequireFile) copied to the output"
		}
	}

	if ($passed -and $case.RequireText.Count -gt 0) {
		foreach ($pattern in $case.RequireText) {
			$found = @(
				Get-ChildItem -Path $directory -Recurse -File -Include *.cs, *.csproj, *.json -ErrorAction SilentlyContinue |
					Select-String -Pattern $pattern -SimpleMatch -ErrorAction SilentlyContinue
			)

			if ($found.Count -eq 0) {
				$passed = $false
				$signal = "$signal; '$pattern' was not generated into the consumer"
			}
			else {
				$signal = "$signal; generated '$pattern'"
			}
		}
	}

	$results.Add(
		[pscustomobject]@{
			Id = $case.Id
			Case = $case.Name
			Expected = $case.Expect
			Observed = $signal
			Passed = $passed
			Output = $output.Trim()
		}
	)

	$colour = if ($passed) { 'Green' } else { 'Red' }
	$verdict = if ($passed) { 'PASS' } else { 'FAIL' }
	Write-Host ("  {0}  {1}  {2}  (expected {3}); observed {4}" -f $verdict, $case.Id, $case.Name, $case.Expect, $signal) -ForegroundColor $colour
}

$failed = @($results | Where-Object { -not $_.Passed })

if ($failed.Count -gt 0) {
	Write-Host ''
	Write-Host 'Failing case output (first 40 lines each):' -ForegroundColor Yellow

	foreach ($result in $failed) {
		Write-Host ''
		Write-Host ("--- {0} {1} ---" -f $result.Id, $result.Case) -ForegroundColor Yellow

		($result.Output -split "`r?`n") | Select-Object -First 40 | ForEach-Object { Write-Host "    $_" }
	}
}

if (-not $Keep) {
	Remove-Item $WorkPath -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("{0}/{1} consumer checks passed." -f ($results.Count - $failed.Count), $results.Count) -ForegroundColor $(if ($failed.Count -eq 0) { 'Green' } else { 'Red' })

if ($failed.Count -gt 0) {
	exit 1
}

