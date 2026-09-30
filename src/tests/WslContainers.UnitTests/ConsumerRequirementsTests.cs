using System.Reflection;
using System.Runtime.Versioning;

namespace Purview.WslContainers;

/// <summary>
/// Guards the consumer contract documented in <c>docs/wiki/Consumer-Requirements.md</c>: every
/// package is a .NET 11 project that targets Windows specifically. A consumer cannot restore the
/// package from anything else, so a drift here has to fail the build rather than quietly
/// invalidate the documentation and the shipped <c>buildTransitive</c> defaults.
/// </summary>
public class ConsumerRequirementsTests
{
	static readonly Assembly Library = typeof(WslContainerRuntime).Assembly;
	static readonly string[] WindowsPlatform = ["Windows10.0.19041.0"];

	[Test]
	public async Task LibraryTargetsNet11()
	{
		var targetFramework = Library.GetCustomAttribute<TargetFrameworkAttribute>();

		await Assert.That(targetFramework).IsNotNull();
		await Assert.That(targetFramework!.FrameworkName).IsEqualTo(".NETCoreApp,Version=v11.0");
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
