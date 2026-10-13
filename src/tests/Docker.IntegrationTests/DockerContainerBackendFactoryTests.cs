namespace Purview.Containers.Docker;

/// <summary>
/// Covers <see cref="DockerContainerBackend.CreateContainer(IContainerConfiguration)"/> against a real
/// daemon. Creating a container builds the underlying Testcontainers container, which connects to the
/// daemon, so this is an integration concern: with no daemon reachable the call throws
/// <c>DockerUnavailableException</c> rather than returning a container. The daemon-free surface of the
/// backend (its name and <c>Create</c>) stays in <c>Docker.UnitTests</c>.
/// </summary>
public class DockerContainerBackendFactoryTests
{
	static Task SkipIfUnavailableAsync() => DockerTest.SkipIfUnavailableAsync();

	[Test]
	public async Task CreateContainer_AdaptsTheConfiguredImageAndName()
	{
		await SkipIfUnavailableAsync();

		ContainerConfiguration configuration = new() { Image = "alpine:3.19", Name = "purview-unit-test" };

		var container = new DockerContainerBackend().CreateContainer(configuration);

		await Assert.That(container).IsTypeOf<DockerContainer>();
		await Assert.That(container.Image).IsEqualTo("alpine:3.19");
		await Assert.That(container.Name).IsEqualTo("purview-unit-test");
	}

	[Test]
	public async Task CreateContainer_GeneratesNoName_WhenTheConfigurationDoesNotSetOne()
	{
		await SkipIfUnavailableAsync();

		ContainerConfiguration configuration = new() { Image = "alpine:3.19" };

		var container = new DockerContainerBackend().CreateContainer(configuration);

		await Assert.That(container.Name).IsNotEmpty();
	}
}
