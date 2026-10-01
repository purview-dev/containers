using System.Reflection;
using System.Runtime.Versioning;

namespace Purview.Containers.Wsl;

/// <summary>
/// Guards the consumer contract documented in <c>docs/wiki/Consumer-Requirements.md</c>. The WSL
/// Containers backend is a multi-target package: a Windows build (the implementation, targeting
/// Windows specifically) and a platform-neutral build (the facade). A Windows-targeting consumer
/// binds the Windows build, which is what this project references, so a drift here has to fail the
/// build rather than quietly invalidate the documentation and the shipped <c>buildTransitive</c>
/// defaults.
/// </summary>
public class ConsumerRequirementsTests
{
	static readonly Assembly Library = typeof(WslContainerRuntime).Assembly;
	static readonly string[] WindowsPlatform = ["Windows10.0.19041.0"];

	[Test]
	public async Task LibraryTargetsNet10()
	{
		var targetFramework = Library.GetCustomAttribute<TargetFrameworkAttribute>();

		await Assert.That(targetFramework).IsNotNull();
		await Assert.That(targetFramework!.FrameworkName).IsEqualTo(".NETCoreApp,Version=v10.0");
	}

	[Test]
	public async Task LibraryTargetsWindows()
	{
		var targetPlatform = Library.GetCustomAttribute<TargetPlatformAttribute>();

		await Assert.That(targetPlatform).IsNotNull();
		await Assert.That(targetPlatform!.PlatformName).IsEqualTo("Windows10.0.19041.0");
	}

	[Test]
	public async Task LibraryIsSupportedOnWindowsOnly()
	{
		var platforms = Library
			.GetCustomAttributes<SupportedOSPlatformAttribute>()
			.Select(attribute => attribute.PlatformName)
			.ToArray();

		await Assert.That(platforms).IsEquivalentTo(WindowsPlatform);
	}
}
