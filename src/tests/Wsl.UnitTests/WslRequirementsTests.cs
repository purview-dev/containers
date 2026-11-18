using System.Runtime.InteropServices;
using Microsoft.WSL.Containers;

namespace Purview.Containers.Wsl;

/// <summary>
/// Guards the WSL Containers host requirements and the text a consumer sees when a host does not meet
/// them: every reason must name its fix rather than a bare SDK enum name or exception type.
/// </summary>
public class WslRequirementsTests
{
	[Test]
	public async Task MinimumWslVersion_IsStampedFromTheCentralPin()
	{
		// Directory.Packages.props: MinimumWslVersion follows MicrosoftWSLContainersVersion (3.0.5).
		await Assert.That(WslRequirements.MinimumWslVersion).IsEqualTo(new Version(3, 0, 5));
		await Assert.That(WslRequirements.SdkVersion).IsEqualTo(new Version(3, 0, 5));
		await Assert.That(WslRequirements.MinimumWslVersion >= WslRequirements.SdkVersion).IsTrue();
	}

	[Test]
	[Arguments(3, 0, 5)]
	[Arguments(3, 0, 6)]
	[Arguments(3, 1, 0)]
	[Arguments(4, 0, 0)]
	public async Task DescribeVersion_AtOrAboveTheFloor_IsAccepted(int major, int minor, int revision)
	{
		await Assert.That(WslRequirements.DescribeVersion(new Version(major, minor, revision))).IsNull();
	}

	[Test]
	[Arguments(3, 0, 3)]
	[Arguments(3, 0, 1)]
	[Arguments(2, 9, 3)]
	public async Task DescribeVersion_BelowTheFloor_NamesTheMinimumAndTheFix(int major, int minor, int revision)
	{
		var reason = WslRequirements.DescribeVersion(new Version(major, minor, revision));

		await Assert.That(reason).IsNotNull();
		await Assert.That(reason!).Contains($"WSL {major}.{minor}.{revision}");
		await Assert.That(reason).Contains("3.0.5");
		await Assert.That(reason).Contains("wsl --update");
	}

	[Test]
	public async Task Describe_VirtualMachinePlatform_ExplainsTheWindowsFeature()
	{
		var reason = WslRequirements.Describe(Component.VirtualMachinePlatform, installed: null);

		await Assert.That(reason).Contains("Virtual Machine Platform");
		await Assert.That(reason).Contains("wsl --install --no-distribution");
		await Assert.That(reason).DoesNotContain(nameof(Component.VirtualMachinePlatform));
	}

	[Test]
	public async Task Describe_WslPackage_GivesTheInstallCommandAndTheMinimum()
	{
		var reason = WslRequirements.Describe(Component.WslPackage, installed: null);

		await Assert.That(reason).Contains("wsl --install --no-distribution");
		await Assert.That(reason).Contains("wsl --update");
		await Assert.That(reason).Contains("3.0.5");
	}

	[Test]
	public async Task Describe_SdkNeedsUpdate_PointsAtThePackageNotWsl()
	{
		var reason = WslRequirements.Describe(Component.SdkNeedsUpdate, new Version(3, 1, 0));

		await Assert.That(reason).Contains("3.1.0");
		await Assert.That(reason).Contains("Update the Purview.Containers.Wsl package");
	}

	[Test]
	public async Task Describe_OSNotSupported_NamesTheSupportedWindowsVersions()
	{
		var reason = WslRequirements.Describe(new COMException("boom", WslRequirements.OSNotSupported));

		await Assert.That(reason).Contains("WSL_E_OS_NOT_SUPPORTED");
		await Assert.That(reason).Contains("Windows 11");
		await Assert.That(reason).Contains("build 19041");
	}

	[Test]
	public async Task Describe_OptionalComponentRequired_GivesTheInstallCommand()
	{
		var reason = WslRequirements.Describe(new COMException("boom", WslRequirements.OptionalComponentRequired));

		await Assert.That(reason).Contains("WSL_E_WSL_OPTIONAL_COMPONENT_REQUIRED");
		await Assert.That(reason).Contains("wsl --install --no-distribution");
	}

	[Test]
	public async Task Describe_ClassNotRegistered_SaysWslIsNotInstalled()
	{
		var reason = WslRequirements.Describe(new COMException("boom", WslRequirements.ClassNotRegistered));

		await Assert.That(reason).Contains("WSL is not installed");
		await Assert.That(reason).Contains("wsl --install --no-distribution");
	}

	[Test]
	public async Task Describe_MissingNativeSdk_ExplainsTheDeployment()
	{
		var reason = WslRequirements.Describe(new DllNotFoundException("wslcsdk.dll"));

		await Assert.That(reason).Contains("wslcsdk.dll");
		await Assert.That(reason).Contains("x64");
	}

	[Test]
	public async Task Describe_UnknownFailure_KeepsTheHResultAndPointsAtTheUpdate()
	{
		var reason = WslRequirements.Describe(new COMException("unexpected", unchecked((int)0x80004005)));

		await Assert.That(reason).Contains("0x80004005");
		await Assert.That(reason).Contains("unexpected");
		await Assert.That(reason).Contains("wsl --update");
	}

	[Test]
	public async Task DescribeUnsupportedProcess_X64_IsSupported()
	{
		await Assert.That(WslRequirements.DescribeUnsupportedProcess(Architecture.X64)).IsNull();
	}

	[Test]
	[Arguments(Architecture.Arm64)]
	[Arguments(Architecture.X86)]
	public async Task DescribeUnsupportedProcess_OtherArchitectures_AreRejected(Architecture architecture)
	{
		var reason = WslRequirements.DescribeUnsupportedProcess(architecture);

		await Assert.That(reason).IsNotNull();
		await Assert.That(reason!).Contains("x64 processes only");
		await Assert.That(reason).Contains(architecture.ToString());
	}
}
