using NATS.Client.Core;

namespace Purview.Containers.Nats;

public class NatsIntegrationTests
{
	[Test]
	public async Task Nats_RespondsToPing()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var nats = new NatsBuilder().Build();

		await nats.StartAsync();

		await WslcTest.WaitForTcpAsync(nats.GetMappedPublicPort(NatsBuilder.ClientPort), TimeSpan.FromSeconds(30));
		await using NatsConnection client = new(new NatsOpts { Url = nats.GetConnectionString() });
		var roundTrip = await client.PingAsync();

		await Assert.That(roundTrip).IsGreaterThan(TimeSpan.Zero);
	}

	[Test]
	public async Task Endpoints_UseMappedRandomPorts()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var nats = new NatsBuilder().Build();

		await nats.StartAsync();

		var clientPort = nats.GetMappedPublicPort(NatsBuilder.ClientPort);
		var monitoringPort = nats.GetMappedPublicPort(NatsBuilder.MonitoringPort);

		await Assert.That(nats.GetClientEndpoint().Host).IsEqualTo("127.0.0.1");
		await Assert.That(nats.GetClientEndpoint().Port).IsEqualTo(clientPort);
		await Assert.That(nats.GetMonitoringEndpoint().Host).IsEqualTo("127.0.0.1");
		await Assert.That(nats.GetMonitoringEndpoint().Port).IsEqualTo(monitoringPort);
	}
}
