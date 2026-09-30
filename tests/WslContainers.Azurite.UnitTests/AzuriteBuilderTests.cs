using Purview.WslContainers;
using Purview.WslContainers.Azurite;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.Azurite.UnitTests;

public class AzuriteBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		AzuriteConfiguration configuration = new AzuriteBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("mcr.microsoft.com/azure-storage/azurite:latest");
		await Assert
			.That(configuration.Command)
			.IsEquivalentTo(
				new[] { "azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0" }
			);
		await Assert.That(configuration.PortBindings).HasCount().EqualTo(3);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 10000 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 10001 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 10002 && p.AssignRandomHostPort);
		await Assert.That(configuration.WaitStrategies).HasCount().EqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<LogMessageWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_SupportsCustomImage()
	{
		AzuriteConfiguration configuration = new AzuriteBuilder("custom/azurite:1").BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/custom/azurite:1");
	}
}
