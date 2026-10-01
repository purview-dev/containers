namespace Purview.Containers.Docker;

/// <summary>
/// Unit coverage for the Docker backend that needs no daemon: identity, registration metadata and the
/// configuration translation. Anything that talks to a daemon lives in <c>Docker.IntegrationTests</c>.
/// </summary>
public class DockerContainerBackendTests
{
	[Test]
	public async Task Name_IsDocker()
	{
		await Assert.That(DockerContainerBackend.Create().Name).IsEqualTo("docker");
	}

	[Test]
	public async Task Create_ReturnsANewBackend()
	{
		await Assert.That(DockerContainerBackend.Create()).IsNotNull();
	}

	[Test]
	public async Task CreateContainer_AdaptsTheConfiguredImageAndName()
	{
		ContainerConfiguration configuration = new() { Image = "alpine:3.19", Name = "purview-unit-test" };

		var container = new DockerContainerBackend().CreateContainer(configuration);

		await Assert.That(container).IsTypeOf<DockerContainer>();
		await Assert.That(container.Image).IsEqualTo("alpine:3.19");
		await Assert.That(container.Name).IsEqualTo("purview-unit-test");
	}

	[Test]
	public async Task CreateContainer_GeneratesNoName_WhenTheConfigurationDoesNotSetOne()
	{
		ContainerConfiguration configuration = new() { Image = "alpine:3.19" };

		var container = new DockerContainerBackend().CreateContainer(configuration);

		await Assert.That(container.Name).IsNotEmpty();
	}
}
