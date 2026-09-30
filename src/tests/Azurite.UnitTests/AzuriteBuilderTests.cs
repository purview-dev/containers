using Purview.WslContainers.Waiting;

namespace Purview.WslContainers.Azurite;

class AzuriteBuilderTests
{
	static readonly string[] Expected = ["azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0"];

	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new AzuriteBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("mcr.microsoft.com/azure-storage/azurite:latest");
		await Assert
			.That(configuration.Command)
			.IsEquivalentTo(
				Expected
			);
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(3);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 10000 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 10001 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 10002 && p.AssignRandomHostPort);
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<LogMessageWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_SupportsCustomImage()
	{
		var configuration = new AzuriteBuilder("custom/azurite:1").BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/custom/azurite:1");
	}
}
