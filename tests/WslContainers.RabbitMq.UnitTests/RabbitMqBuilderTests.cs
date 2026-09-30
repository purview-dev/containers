using Purview.WslContainers;
using Purview.WslContainers.RabbitMq;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.RabbitMq.UnitTests;

public class RabbitMqBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		RabbitMqConfiguration configuration = new RabbitMqBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/rabbitmq:3-management");
		await Assert.That(configuration.Username).IsEqualTo("guest");
		await Assert.That(configuration.Password.Value).IsEqualTo("guest");
		await Assert.That(configuration.VirtualHost).IsEqualTo("/");
		await Assert.That(configuration.PortBindings).HasCount().EqualTo(2);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 5672);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 15672);
		await Assert.That(configuration.WaitStrategies).HasCount().EqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<LogMessageWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_HonoursModuleSetters()
	{
		RabbitMqConfiguration configuration = new RabbitMqBuilder()
			.WithUsername("app")
			.WithPassword("s3cret")
			.WithVirtualHost("myvhost")
			.BuildConfigurationForTesting();

		await Assert.That(configuration.Username).IsEqualTo("app");
		await Assert.That(configuration.Password.Value).IsEqualTo("s3cret");
		await Assert.That(configuration.VirtualHost).IsEqualTo("myvhost");
		await Assert.That(configuration.Environment["RABBITMQ_DEFAULT_USER"]).IsEqualTo("app");
		await Assert.That(configuration.Environment["RABBITMQ_DEFAULT_PASS"]).IsEqualTo("s3cret");
		await Assert.That(configuration.Environment["RABBITMQ_DEFAULT_VHOST"]).IsEqualTo("myvhost");
	}

	[Test]
	public async Task BuildConfig_EmptyPassword_Throws()
	{
		RabbitMqBuilder builder = new RabbitMqBuilder().WithPassword(string.Empty);

		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}
}
