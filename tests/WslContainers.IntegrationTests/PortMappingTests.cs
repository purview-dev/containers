using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Purview.WslContainers;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.IntegrationTests;

public class PortMappingTests
{
	[Test]
	public async Task RandomHostPort_IsAssignedAndReachable()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using WslContainer container = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.Build();

		await container.StartAsync();
		await Assert.That(container.State).IsEqualTo(ContainerState.Running);

		ushort hostPort = container.GetMappedPublicPort(8080);
		await Assert.That(hostPort).IsGreaterThan((ushort)0);

		await WslcTest.WaitForTcpAsync(hostPort, TimeSpan.FromSeconds(30));
	}

	[Test]
	public async Task ExplicitHostPort_IsUsed()
	{
		await WslcTest.SkipIfUnavailableAsync();

		ushort hostPort = GetFreeTcpPort();
		await using WslContainer container = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, hostPort)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.Build();

		await container.StartAsync();

		await Assert.That(container.GetMappedPublicPort(8080)).IsEqualTo(hostPort);
		await WslcTest.WaitForTcpAsync(hostPort, TimeSpan.FromSeconds(30));
	}

	[Test]
	public async Task MultipleContainers_ReceiveDistinctRandomPorts()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using WslContainer first = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.Build();
		await using WslContainer second = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.Build();

		await Task.WhenAll(first.StartAsync(), second.StartAsync());

		ushort firstPort = first.GetMappedPublicPort(8080);
		ushort secondPort = second.GetMappedPublicPort(8080);
		await Assert.That(firstPort).IsNotEqualTo(secondPort);
	}

	private static ushort GetFreeTcpPort()
	{
		using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
	}
}
