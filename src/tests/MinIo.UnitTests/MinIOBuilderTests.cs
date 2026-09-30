using Purview.WslContainers.Runtime;

namespace Purview.WslContainers.MinIO;

class MinIOBuilderTests
{
	static readonly string[] Expected = ["minio", "server", "/data", "--console-address", ":9001"];

	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new MinIOBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("quay.io/minio/minio:latest");
		await Assert.That(configuration.AccessKey).IsEqualTo("minioadmin");
		await Assert.That(configuration.SecretKey.Value).IsEqualTo("minioadmin");
		await Assert.That(configuration.Environment["MINIO_ROOT_USER"]).IsEqualTo("minioadmin");
		await Assert.That(configuration.Environment["MINIO_ROOT_PASSWORD"]).IsEqualTo("minioadmin");
		await Assert
			.That(configuration.Command)
			.IsEquivalentTo(Expected);
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(2);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 9000 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 9001 && p.AssignRandomHostPort);
	}

	[Test]
	public async Task BuildConfig_EmptySecret_Throws()
	{
		var builder = new MinIOBuilder().WithSecretKey(string.Empty);

		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}
}
