using Purview.Containers.Waiting;

namespace Purview.Containers.Wsl;

[Explicit]
public class NetworkTests
{
	[Test]
	public async Task ManagedNetwork_ContainerAttachesAndNetworkIsRemoved(CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		WslContainerBackend backend = new();
		var name = $"purview-net-{Guid.NewGuid():N}"[..28];
		await using (
			var network = await backend.CreateNetworkAsync(new NetworkConfiguration { Name = name }, cancellationToken)
		)
		{
			await using var container = new ContainerBuilder(backend)
				.WithImage("alpine:latest")
				.WithNetwork(network.Name)
				.WithNetworkAliases("peer")
				.WithCommand("/bin/sleep", "300")
				.Build();

			await container.StartAsync(cancellationToken);

			// The managed network replaced the default bridge, so the container reports an address on it.
			await Assert.That(container.NetworkIp).IsNotNull().And.IsNotEmpty();
		}

		// Disposing the resource deletes the session network, so the same name can be created again.
		await using var recreated = await backend.CreateNetworkAsync(
			new NetworkConfiguration { Name = name },
			cancellationToken
		);
		await Assert.That(recreated.Name).IsEqualTo(name);
	}

	[Test]
	public async Task NetworkAliases_ResolveBetweenContainers(CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		WslContainerBackend backend = new();
		await using var network = await backend.CreateNetworkAsync(cancellationToken: cancellationToken);

		await using var listener = new ContainerBuilder(backend)
			.WithImage("alpine:latest")
			.WithNetwork(network.Name)
			.WithNetworkAliases("peer")
			.WithCommand("/bin/sleep", "300")
			.Build();
		await listener.StartAsync(cancellationToken);

		await using var client = new ContainerBuilder(backend)
			.WithImage("alpine:latest")
			.WithNetwork(network.Name)
			.WithCommand("/bin/sh", "-c", "ping -c 1 -W 2 peer")
			.WithWaitStrategy(Wait.ForExitCode())
			.Build();
		await client.StartAsync(cancellationToken);

		// The exit-code wait fails the start when the alias does not resolve.
		await Assert.That(client.State).IsEqualTo(ContainerState.Exited);
	}
}
