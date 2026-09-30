using Purview.WslContainers.Waiting;

namespace Purview.WslContainers;

class ConcurrencyStressTests
{
	[Test]
	public async Task ConcurrentContainers_GetDistinctUsablePorts()
	{
		await WslcTest.SkipIfUnavailableAsync();

		const int count = 5;
		List<WslContainer> containers = [with(count)];
		try
		{
			for (var i = 0; i < count; i++)
			{
				containers.Add(
					new ContainerBuilder()
						.WithImage("python:3-alpine")
						.WithPortBinding(8080, assignRandomHostPort: true)
						.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
						.WithWaitStrategy(Wait.ForTcpPort(8080))
						.Build()
				);
			}

			await Task.WhenAll(containers.Select(container => container.StartAsync()));

			IEnumerable<ushort> ports = containers.Select(container => container.GetMappedPublicPort(8080)).ToList();
			await Assert.That(ports.Distinct().Count()).IsEqualTo(count);
			foreach (int port in ports)
			{
				await WslcTest.WaitForTcpAsync(port, TimeSpan.FromSeconds(15));
			}
		}
		finally
		{
			foreach (var container in containers)
			{
				await container.DisposeAsync();
			}
		}
	}
}
