using System.Text;
using Purview.WslContainers.RabbitMq;
using Purview.WslContainers.Testing;
using RabbitMQ.Client;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.RabbitMq.IntegrationTests;

public class RabbitMqIntegrationTests
{
	[Test]
	public async Task RabbitMq_PublishesAndConsumes()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using RabbitMqContainer rabbit = new RabbitMqBuilder()
			.WithUsername("guest")
			.WithPassword("guest")
			.Build();

		await rabbit.StartAsync();

		ConnectionFactory factory = new ConnectionFactory { Uri = rabbit.GetAmqpEndpoint() };
		using IConnection connection = await factory.CreateConnectionAsync();
		using IChannel channel = await connection.CreateChannelAsync();

		const string queue = "wslc-test-queue";
		await channel.QueueDeclareAsync(queue, durable: false, exclusive: false, autoDelete: true);
		await channel.BasicPublishAsync(
			exchange: string.Empty,
			routingKey: queue,
			body: Encoding.UTF8.GetBytes("hello-rabbitmq")
		);

		BasicGetResult? result = await channel.BasicGetAsync(queue, autoAck: true);

		await Assert.That(result).IsNotNull();
		await Assert.That(Encoding.UTF8.GetString(result!.Body.ToArray())).IsEqualTo("hello-rabbitmq");
	}

	[Test]
	public async Task Endpoints_UseMappedRandomPorts()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using RabbitMqContainer rabbit = new RabbitMqBuilder().Build();

		await rabbit.StartAsync();

		ushort amqpPort = rabbit.GetMappedPublicPort(RabbitMqBuilder.AmqpPort);
		ushort managementPort = rabbit.GetMappedPublicPort(RabbitMqBuilder.ManagementPort);

		await Assert.That(rabbit.GetAmqpEndpoint().ToString()).IsEqualTo($"amqp://guest:guest@localhost:{amqpPort}/");
		await Assert.That(rabbit.GetManagementEndpoint().ToString()).IsEqualTo($"http://localhost:{managementPort}/");
		await Assert.That(amqpPort).IsNotEqualTo(managementPort);
	}
}
