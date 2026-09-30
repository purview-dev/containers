using Purview.WslContainers;
using Purview.WslContainers.Redis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.Redis.UnitTests;

public class RedisBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		RedisConfiguration configuration = new RedisBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/redis:7");
		await Assert.That(configuration.PortBindings).HasCount().EqualTo(1);
		await Assert.That(configuration.PortBindings[0].ContainerPort).IsEqualTo((ushort)6379);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsTrue();
		await Assert.That(configuration.WaitStrategies).HasCount().EqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<CommandWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_SupportsCompatibleImages()
	{
		RedisConfiguration configuration = new RedisBuilder("valkey/valkey:7").BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/valkey/valkey:7");
	}

	[Test]
	public async Task BuildConfig_PreservesCustomWaitStrategy()
	{
		RedisConfiguration configuration = new RedisBuilder()
			.WithWaitStrategy(Wait.ForTcpPort(6379))
			.BuildConfigurationForTesting();

		await Assert.That(configuration.WaitStrategies).HasCount().EqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<TcpPortWaitStrategy>();
	}
}
