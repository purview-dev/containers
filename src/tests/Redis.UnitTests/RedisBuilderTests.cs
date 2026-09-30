using Purview.WslContainers.Waiting;

namespace Purview.WslContainers.Redis;

class RedisBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new RedisBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/redis:7");
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(1);
		await Assert.That(configuration.PortBindings[0].ContainerPort).IsEqualTo((ushort)6379);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsTrue();
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<CommandWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_SupportsCompatibleImages()
	{
		var configuration = new RedisBuilder("valkey/valkey:7").BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/valkey/valkey:7");
	}

	[Test]
	public async Task BuildConfig_PreservesCustomWaitStrategy()
	{
		var configuration = new RedisBuilder()
			.WithWaitStrategy(Wait.ForTcpPort(6379))
			.BuildConfigurationForTesting();

		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<TcpPortWaitStrategy>();
	}
}
