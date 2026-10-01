namespace Purview.Containers.Docker;

/// <summary>
/// Covers backend selection with a real daemon: the generated registration analogue (explicit
/// registration), a named selection, and a container produced by the generic builder through the
/// registry. The registry is process-wide static state, so this class is not run in parallel.
/// </summary>
[NotInParallel]
public class DockerBackendSelectionTests
{
	static Task SkipIfUnavailableAsync() => DockerTest.SkipIfUnavailableAsync();

	[Test]
	public async Task GenericBuilder_WithTheDockerBackendSelected_StartsAContainer()
	{
		await SkipIfUnavailableAsync();
		ContainerBackends.Reset();
		try
		{
			ContainerBackends.Register(DockerContainerBackend.Create());
			ContainerBackends.Use(ContainerBackendSelection.Named("docker"));

			// A long-lived command: with a bare `/bin/echo` the container can exit before the state is
			// read, making the Running assertion below a race.
			await using var container = new ContainerBuilder()
				.WithImage("alpine:3.19")
				.WithCommand("/bin/sh", "-c", "echo selected && sleep 60")
				.Build();

			await container.StartAsync();

			var logs = await container.GetLogsAsync();

			await Assert.That(container.State).IsEqualTo(ContainerState.Running);
			await Assert.That(logs).Contains("selected");
		}
		finally
		{
			ContainerBackends.Reset();
		}
	}

	[Test]
	public async Task ResolveAsync_WithAutomaticDetection_SelectsTheRegisteredDockerBackend()
	{
		await SkipIfUnavailableAsync();
		ContainerBackends.Reset();
		try
		{
			var backend = DockerContainerBackend.Create();
			ContainerBackends.Register(backend);

			var resolved = await ContainerBackends.ResolveAsync();

			await Assert.That(resolved.Name).IsEqualTo("docker");
		}
		finally
		{
			ContainerBackends.Reset();
		}
	}
}
