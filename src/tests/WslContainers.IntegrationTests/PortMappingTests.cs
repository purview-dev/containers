using System.Net;
using System.Net.Sockets;
using Purview.WslContainers.Containers;

namespace Purview.WslContainers;

class PortMappingTests
{
	[Test]
	public async Task RandomHostPort_IsAssignedAndReachable()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.Build();

		await container.StartAsync();
		await Assert.That(container.State).IsEqualTo(ContainerState.Running);

		var hostPort = container.GetMappedPublicPort(8080);
		await Assert.That(hostPort).IsGreaterThan((ushort)0);

		await WslcTest.WaitForTcpAsync(hostPort, TimeSpan.FromSeconds(30));
	}

	[Test]
	public async Task ExplicitHostPort_IsUsed()
	{
		await WslcTest.SkipIfUnavailableAsync();

		var hostPort = GetFreeTcpPort();
		await using var container = new ContainerBuilder()
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

		await using var first = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.Build();
		await using var second = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.Build();

		await Task.WhenAll(first.StartAsync(), second.StartAsync());

		var firstPort = first.GetMappedPublicPort(8080);
		var secondPort = second.GetMappedPublicPort(8080);
		await Assert.That(firstPort).IsNotEqualTo(secondPort);
	}

	static ushort GetFreeTcpPort()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
	}
}
