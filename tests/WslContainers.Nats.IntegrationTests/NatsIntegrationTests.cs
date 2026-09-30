using NATS.Client.Core;
using Purview.WslContainers.Nats;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.Nats.IntegrationTests;

public class NatsIntegrationTests
{
	[Test]
	public async Task Nats_RespondsToPing()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using NatsContainer nats = new NatsBuilder().Build();

		await nats.StartAsync();

		await WslcTest.WaitForTcpAsync(nats.GetMappedPublicPort(NatsBuilder.ClientPort), TimeSpan.FromSeconds(30));
		await using NatsConnection client = new NatsConnection(new NatsOpts { Url = nats.GetConnectionString() });
		TimeSpan roundTrip = await client.PingAsync();

		await Assert.That(roundTrip).IsGreaterThan(TimeSpan.Zero);
	}

	[Test]
	public async Task Endpoints_UseMappedRandomPorts()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using NatsContainer nats = new NatsBuilder().Build();

		await nats.StartAsync();

		ushort clientPort = nats.GetMappedPublicPort(NatsBuilder.ClientPort);
		ushort monitoringPort = nats.GetMappedPublicPort(NatsBuilder.MonitoringPort);

		await Assert.That(nats.GetClientEndpoint().Host).IsEqualTo("127.0.0.1");
		await Assert.That(nats.GetClientEndpoint().Port).IsEqualTo(clientPort);
		await Assert.That(nats.GetMonitoringEndpoint().Host).IsEqualTo("127.0.0.1");
		await Assert.That(nats.GetMonitoringEndpoint().Port).IsEqualTo(monitoringPort);
	}
}
