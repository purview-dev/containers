using System.Runtime.CompilerServices;

namespace Purview.Containers.Docker;

/// <summary>
/// Test-process defaults for Testcontainers' resource reaper.
/// </summary>
/// <remarks>
/// Ryuk is valuable for a consumer process that may crash, but in these tests every container is
/// disposed explicitly, and Ryuk's own random public host port is a real source of flakiness on a
/// Windows host: WSL's relay can already own the loopback port that Ryuk picks, so the container starts
/// but the reaper cannot be reached and Testcontainers fails the first start with
/// <c>ResourceReaperException: Initialization has been cancelled</c> after a minute. Disabling the
/// reaper for this test process keeps the suite deterministic; set
/// <c>TESTCONTAINERS_RYUK_DISABLED=false</c> to opt back in.
/// </remarks>
static class DockerTestEnvironment
{
	[ModuleInitializer]
	internal static void Initialize()
	{
		if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED")))
		{
			Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
		}
	}
}
