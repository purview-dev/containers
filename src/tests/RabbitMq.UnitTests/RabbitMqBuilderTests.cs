using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.RabbitMq;

public class RabbitMqBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new RabbitMqBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/rabbitmq:3-management");
		await Assert.That(configuration.Username).IsEqualTo("guest");
		await Assert.That(configuration.Password.Value).IsEqualTo("guest");
		await Assert.That(configuration.VirtualHost).IsEqualTo("/");
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(2);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 5672);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 15672);
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<LogMessageWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_HonoursModuleSetters()
	{
		var configuration = new RabbitMqBuilder()
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
		var builder = new RabbitMqBuilder().WithPassword(string.Empty);

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}
}
