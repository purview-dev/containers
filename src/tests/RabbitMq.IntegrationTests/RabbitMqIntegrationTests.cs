using System.Text;
using RabbitMQ.Client;

namespace Purview.Containers.RabbitMq;

public class RabbitMqIntegrationTests
{
	[Test]
	public async Task RabbitMq_PublishesAndConsumes()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var rabbit = new RabbitMqBuilder().WithUsername("guest").WithPassword("guest").Build();

		await rabbit.StartAsync();

		ConnectionFactory factory = new() { Uri = rabbit.GetAmqpEndpoint() };
		using var connection = await factory.CreateConnectionAsync();
		using var channel = await connection.CreateChannelAsync();

		const string queue = "wslc-test-queue";
		await channel.QueueDeclareAsync(queue, durable: false, exclusive: false, autoDelete: true);
		await channel.BasicPublishAsync(
			exchange: string.Empty,
			routingKey: queue,
			body: Encoding.UTF8.GetBytes("hello-rabbitmq")
		);

		var result = await channel.BasicGetAsync(queue, autoAck: true);

		await Assert.That(result).IsNotNull();
		await Assert.That(Encoding.UTF8.GetString(result!.Body.ToArray())).IsEqualTo("hello-rabbitmq");
	}

	[Test]
	public async Task Endpoints_UseMappedRandomPorts()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var rabbit = new RabbitMqBuilder().Build();

		await rabbit.StartAsync();

		var amqpPort = rabbit.GetMappedPublicPort(RabbitMqBuilder.AmqpPort);
		var managementPort = rabbit.GetMappedPublicPort(RabbitMqBuilder.ManagementPort);

		await Assert.That(rabbit.GetAmqpEndpoint().ToString()).IsEqualTo($"amqp://guest:guest@localhost:{amqpPort}/");
		await Assert.That(rabbit.GetManagementEndpoint().ToString()).IsEqualTo($"http://localhost:{managementPort}/");
		await Assert.That(amqpPort).IsNotEqualTo(managementPort);
	}
}
