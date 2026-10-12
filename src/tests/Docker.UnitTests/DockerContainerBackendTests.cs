namespace Purview.Containers.Docker;

/// <summary>
/// Unit coverage for the Docker backend that needs no daemon: identity and registration metadata.
/// Anything that talks to a daemon lives in <c>Docker.IntegrationTests</c> — including
/// <c>CreateContainer</c>, which builds the underlying Testcontainers container and therefore connects.
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
}
