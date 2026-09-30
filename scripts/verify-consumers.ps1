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
	  03  ...with PlatformTarget=x86                                  -> fails PWC0002
	  04  ...with no settings at all (buildTransitive defaults)       -> builds, wslcsdk.dll copied
	  05  a module package with no settings (defaults are transitive)  -> builds
	  06  net8.0-windows + AssetTargetFallback escape hatch           -> PWC0001 (the hatch does not work)
	  07  net8.0-windows without the escape hatch                     -> PWC0001
	  08  plain net11.0 (not Windows-specific)                        -> PWC0001
	  09  ...with PlatformTarget=AnyCPU (corrected to x64)            -> builds, wslcsdk.dll copied
	  10  the net11.0-windows shorthand TFM (no OS version)           -> PWC0001
	  11  multi-targeting with a conditional PackageReference          -> builds
	  12  multi-targeting with an unconditional PackageReference       -> PWC0001

.PARAMETER FeedPath
	Folder holding the packed .nupkg files. Defaults to <repo>/artifacts.

.PARAMETER PackageVersion
	Package version to consume. Defaults to the version in package.json.

.PARAMETER WorkPath
	Scratch folder for the generated consumers. Defaults to
	%TEMP%/wslc-consumer-verification.

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

$corePackage = Join-Path $FeedPath "Purview.WslContainers.$PackageVersion.nupkg"
if (-not (Test-Path $corePackage)) {
	throw "Package '$corePackage' was not found. Run 'just pack' (or pass -FeedPath) first."
}

$globalPackagesLine = & dotnet nuget locals global-packages --list | Select-Object -First 1
$globalPackages = ($globalPackagesLine -replace '^global-packages:\s*', '').Trim()

# The packages keep the same version between local packs, so a stale extraction in the global
# packages folder would silently test the previous build. Drop it before consuming the feed.
foreach ($packageId in 'purview.wslcontainers', 'purview.wslcontainers.postgresql') {
	$packageFolder = Join-Path $globalPackages $packageId
	$versioned = Join-Path $packageFolder $PackageVersion

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
using Purview.WslContainers;

namespace Consumer;

public static class Program
{
	public static SessionSettings Session() => new("consumer-session", @"C:\temp\wslc");

	public static WslContainer Container() =>
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
$sdkPackage = 'Purview.WslContainers'
$modulePackage = 'Purview.WslContainers.PostgreSql'
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
		[string] $RequireFile = ''
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
	}
}

$cases = @(
	New-ConsumerCase -Id '01' -Name 'net11 windows + documented settings' `
		-Properties "$sdkVersion$x64" -RequireFile 'wslcsdk.dll'

	New-ConsumerCase -Id '02' -Name 'net11 windows + stale WindowsSdkPackageVersion' `
		-Properties "$staleSdkVersion$x64" -Expect 'CS1705'

	New-ConsumerCase -Id '03' -Name 'net11 windows + PlatformTarget=x86' `
		-Properties "$sdkVersion<PlatformTarget>x86</PlatformTarget>" -Expect 'PWC0002'

	New-ConsumerCase -Id '04' -Name 'net11 windows + buildTransitive defaults only' `
		-RequireFile 'wslcsdk.dll'

	New-ConsumerCase -Id '05' -Name 'module package + transitive defaults only' `
		-Package $modulePackage

	New-ConsumerCase -Id '06' -Name 'net8 windows + AssetTargetFallback escape hatch' `
		-Framework "<TargetFramework>$net8Windows</TargetFramework>" `
		-Properties "$escapeHatch$sdkVersion$x64" -Expect 'PWC0001'

	New-ConsumerCase -Id '07' -Name 'net8 windows without the escape hatch' `
		-Framework "<TargetFramework>$net8Windows</TargetFramework>" `
		-Properties "$sdkVersion$x64" -Expect 'PWC0001'

	New-ConsumerCase -Id '08' -Name 'plain net11.0 (not Windows-specific)' `
		-Framework '<TargetFramework>net11.0</TargetFramework>' `
		-Properties "$sdkVersion$x64" -Expect 'PWC0001'

	New-ConsumerCase -Id '09' -Name 'net11 windows + PlatformTarget=AnyCPU' `
		-Properties "$sdkVersion<PlatformTarget>AnyCPU</PlatformTarget>" -RequireFile 'wslcsdk.dll'

	New-ConsumerCase -Id '10' -Name 'net11.0-windows shorthand TFM (no OS version)' `
		-Framework '<TargetFramework>net11.0-windows</TargetFramework>' `
		-Properties "$sdkVersion$x64" -Expect 'PWC0001'

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
		-Sources @{ 'Smoke.Windows.cs' = $apiSource; 'Smoke.Portable.cs' = $portableSource } `
		-Expect 'PWC0001'
)


Write-Host ''
Write-Host 'Purview.WslContainers consumer verification' -ForegroundColor Cyan
Write-Host "  feed    : $FeedPath"
Write-Host "  version : $PackageVersion"
Write-Host "  scratch : $WorkPath"
Write-Host ''

$results = [System.Collections.Generic.List[object]]::new()

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

