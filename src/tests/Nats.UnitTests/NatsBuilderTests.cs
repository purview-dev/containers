using Purview.Containers.Waiting;

namespace Purview.Containers.Nats;

public class NatsBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new NatsBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/nats:2");
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(2);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 4222 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 8222 && p.AssignRandomHostPort);
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<LogMessageWaitStrategy>();
	}
}
