using System.Net;
using System.Net.Sockets;

namespace Purview.Containers.Wsl;

[Explicit]
public class PortMappingTests
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

		// The port is chosen by probing and then released before the container binds it, so another
		// process can take it in between. Retry a couple of times rather than failing the suite on a
		// lost race; the point of the test is that an explicit mapping is honoured, not the probe.
		Container? container = null;
		ushort hostPort = 0;
		try
		{
			for (var attempt = 0; attempt < 3 && container is null; attempt++)
			{
				var candidatePort = GetFreeTcpPort();
				var candidate = new ContainerBuilder()
					.WithImage("python:3-alpine")
					.WithPortBinding(8080, candidatePort)
					.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
					.Build();
				try
				{
					await candidate.StartAsync();
					container = candidate;
					hostPort = candidatePort;
				}
				catch
				{
					await candidate.DisposeAsync();
					if (attempt == 2)
					{
						throw;
					}
				}
			}

			await Assert.That(container).IsNotNull();
			await Assert.That(container!.GetMappedPublicPort(8080)).IsEqualTo(hostPort);
			await WslcTest.WaitForTcpAsync(hostPort, TimeSpan.FromSeconds(30));
		}
		finally
		{
			if (container is not null)
			{
				await container.DisposeAsync();
			}
		}
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
