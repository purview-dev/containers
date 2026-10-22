using System.Runtime.CompilerServices;

namespace Purview.Containers.Modules.Docker;

/// <summary>
/// Test-process defaults for Testcontainers' resource reaper. See the Docker contract suite's
/// <c>DockerTestEnvironment</c>: every container is disposed explicitly, and Ryuk's random public port
/// collides with WSL relays often enough on Windows to fail the first start after a minute.
/// </summary>
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
