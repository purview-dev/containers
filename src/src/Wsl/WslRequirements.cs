using System.Reflection;
using System.Runtime.InteropServices;
using Sdk = Microsoft.WSL.Containers;

namespace Purview.Containers.Wsl;

/// <summary>
/// The host requirements of the WSL Containers backend, and the actionable text reported when a host
/// does not meet them. Everything a consumer sees from <c>GetInfoAsync</c> on an unusable host comes
/// from here, so each reason names its fix instead of a bare SDK enum name or exception type.
/// </summary>
/// <remarks>
/// The backend deliberately carries no Windows build check of its own: WSL owns that floor and revises
/// it, so the SDK is asked instead (<c>WslcService.GetMissingComponents</c>, and the
/// <c>WSL_E_OS_NOT_SUPPORTED</c> HRESULT it raises on an unsupported Windows). The one floor enforced
/// here is the WSL version, because the bundled SDK needs a WSL at least as new as itself.
/// </remarks>
/// <summary>
/// The WSL Containers SDK's missing-component flags (<c>Microsoft.WSL.Containers.Component</c>), mirrored
/// here so describing a component never touches the SDK assembly: its module initializer loads the
/// Windows-only native <c>wslcsdk.dll</c>, which would fail anywhere else (for example the Linux CI
/// agent that runs the unit tests). Each value is the SDK's own enum constant, which the compiler folds
/// into this assembly: the values cannot drift, and nothing here loads the SDK at run time.
/// </summary>
enum WslComponent
{
	VirtualMachinePlatform = Sdk.Component.VirtualMachinePlatform,
	WslPackage = Sdk.Component.WslPackage,
	SdkNeedsUpdate = Sdk.Component.SdkNeedsUpdate,
}

static class WslRequirements
{
	/// <summary><c>WSL_E_OS_NOT_SUPPORTED</c>: the Windows version cannot run WSL Containers.</summary>
	internal const int OSNotSupported = unchecked((int)0x80040327);

	/// <summary><c>WSL_E_WSL_OPTIONAL_COMPONENT_REQUIRED</c>: the WSL optional component is not enabled.</summary>
	internal const int OptionalComponentRequired = unchecked((int)0x80040321);

	/// <summary><c>REGDB_E_CLASSNOTREG</c>: the WSL service is not installed or registered.</summary>
	internal const int ClassNotRegistered = unchecked((int)0x80040154);

	const string InstallCommand = "wsl --install --no-distribution";
	const string UpdateCommand = "wsl --update";

	/// <summary>The oldest WSL release the backend accepts (stamped from Directory.Packages.props).</summary>
	internal static Version MinimumWslVersion { get; } =
		ReadMetadataVersion("Purview.Containers.Wsl.MinimumWslVersion");

	/// <summary>The bundled Microsoft.WSL.Containers SDK version (stamped from Directory.Packages.props).</summary>
	internal static Version SdkVersion { get; } = ReadMetadataVersion("Purview.Containers.Wsl.SdkVersion");

	/// <summary>
	/// The backend runs only in an x64 process: the Windows implementation is compiled for x64 and only
	/// the x64 native SDK ships. Returns the reason when this process cannot run it, otherwise <c>null</c>.
	/// </summary>
	internal static string? DescribeUnsupportedProcess(Architecture architecture) =>
		architecture == Architecture.X64
			? null
			: $"The WSL Containers backend supports x64 processes only; this process is {architecture}. "
				+ "Run the tests as an x64 process, or use the Docker backend.";

	/// <summary>The fix for one component the SDK reports as missing.</summary>
	internal static string Describe(WslComponent component, Version? installed) =>
		component switch
		{
			WslComponent.VirtualMachinePlatform =>
				"The Virtual Machine Platform Windows feature is not enabled. Enable virtualisation in the "
					+ $"firmware, run '{InstallCommand}' from an elevated terminal (or enable 'Virtual Machine "
					+ "Platform' in Windows Features), and restart Windows.",
			WslComponent.WslPackage =>
				$"WSL is not installed, or is too old for WSL Containers. Run '{InstallCommand}' "
					+ $"(or '{UpdateCommand}' if WSL is already installed); WSL {MinimumWslVersion} or later is required.",
			WslComponent.SdkNeedsUpdate =>
				$"The installed WSL{(installed is null ? string.Empty : $" ({installed})")} is "
					+ $"newer than the WSL Containers SDK bundled with Purview.Containers.Wsl ({SdkVersion}). Update the "
					+ "Purview.Containers.Wsl package to a release built against this WSL.",
			_ => $"WSL Containers reports a missing component: {component}. Run '{UpdateCommand}'.",
		};

	/// <summary>
	/// The reason <paramref name="installed" /> is below <see cref="MinimumWslVersion" />, or <c>null</c>
	/// when it is new enough.
	/// </summary>
	internal static string? DescribeVersion(Version installed) =>
		installed >= MinimumWslVersion
			? null
			: $"WSL {installed} is older than WSL {MinimumWslVersion}, the minimum this version of "
				+ $"Purview.Containers.Wsl supports. Run '{UpdateCommand}'.";

	/// <summary>The fix for an exception raised while probing the WSL Containers service.</summary>
	internal static string Describe(Exception exception) =>
		exception.HResult switch
		{
			OSNotSupported =>
				"This version of Windows cannot run WSL Containers (WSL_E_OS_NOT_SUPPORTED). Use Windows 11, or "
					+ "Windows 10 version 2004 (build 19041) or later with current updates, then run "
					+ $"'{UpdateCommand}'.",
			OptionalComponentRequired => "The Windows Subsystem for Linux optional component is not enabled "
				+ $"(WSL_E_WSL_OPTIONAL_COMPONENT_REQUIRED). Run '{InstallCommand}' from an elevated terminal "
				+ "and restart Windows.",
			ClassNotRegistered => $"WSL is not installed (the WSL service is not registered). Run '{InstallCommand}', "
				+ $"or '{UpdateCommand}' if WSL is already installed; WSL {MinimumWslVersion} or later is required.",
			_ when exception is DllNotFoundException or BadImageFormatException =>
				"The native WSL Containers SDK (wslcsdk.dll) could not be loaded. The backend needs an x64 process "
					+ "with wslcsdk.dll next to the application, which Purview.Containers.Wsl deploys; rebuild "
					+ $"the project. ({exception.GetType().Name}: {exception.Message})",
			_ => $"WSL Containers could not be queried ({exception.GetType().Name} 0x{exception.HResult:X8}: "
				+ $"{exception.Message}). Check that WSL {MinimumWslVersion} or later is installed ('{UpdateCommand}').",
		};

	static Version ReadMetadataVersion(string key)
	{
		var value = typeof(WslRequirements)
			.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == key)
			?.Value;
		return Version.TryParse(value, out var version)
			? version
			: throw new InvalidOperationException($"The '{key}' assembly metadata is missing or not a version.");
	}
}
