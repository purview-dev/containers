using Purview.WslContainers;
using Purview.WslContainers.Nats;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.Nats.UnitTests;

public class NatsBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		NatsConfiguration configuration = new NatsBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/nats:2");
		await Assert.That(configuration.PortBindings).HasCount().EqualTo(2);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 4222 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 8222 && p.AssignRandomHostPort);
		await Assert.That(configuration.WaitStrategies).HasCount().EqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<LogMessageWaitStrategy>();
	}
}
