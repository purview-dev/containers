using Purview.WslContainers;
using Purview.WslContainers.MinIo;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.MinIo.UnitTests;

public class MinIoBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		MinIoConfiguration configuration = new MinIoBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("quay.io/minio/minio:latest");
		await Assert.That(configuration.AccessKey).IsEqualTo("minioadmin");
		await Assert.That(configuration.SecretKey.Value).IsEqualTo("minioadmin");
		await Assert.That(configuration.Environment["MINIO_ROOT_USER"]).IsEqualTo("minioadmin");
		await Assert.That(configuration.Environment["MINIO_ROOT_PASSWORD"]).IsEqualTo("minioadmin");
		await Assert
			.That(configuration.Command)
			.IsEquivalentTo(new[] { "minio", "server", "/data", "--console-address", ":9001" });
		await Assert.That(configuration.PortBindings).HasCount().EqualTo(2);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 9000 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 9001 && p.AssignRandomHostPort);
	}

	[Test]
	public async Task BuildConfig_EmptySecret_Throws()
	{
		MinIoBuilder builder = new MinIoBuilder().WithSecretKey(string.Empty);

		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}
}
