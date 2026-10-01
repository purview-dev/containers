using TUnit.Core.Exceptions;

namespace Purview.Containers.Docker;

/// <summary>
/// Shared setup for the Docker integration tests. A package consumer gets backend registration from the
/// generated module initializer in <c>Purview.Containers.Backends.targets</c>; this project references the
/// backend as a project, so it registers explicitly here. Idempotent.
/// </summary>
public static class DockerTest
{
	static int BackendRegistered;

	/// <summary>Registers the Docker backend with the shared registry.</summary>
	public static void EnsureBackendRegistered()
	{
		if (Interlocked.Exchange(ref BackendRegistered, 1) == 0)
		{
			ContainerBackends.Register(DockerContainerBackend.Create());
		}
	}

	/// <summary>Skips the current test when no Docker daemon is reachable.</summary>
	public static async Task SkipIfUnavailableAsync()
	{
		EnsureBackendRegistered();
		var info = await DockerContainerBackend.Create().GetInfoAsync();
		if (!info.IsUsable)
		{
			throw new SkipTestException($"Docker is unavailable: {string.Join("; ", info.MissingComponents)}");
		}
	}
}
