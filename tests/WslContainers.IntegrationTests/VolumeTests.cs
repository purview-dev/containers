using Purview.WslContainers;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.IntegrationTests;

public class VolumeTests
{
	[Test]
	public async Task BindMount_ReadsHostFile()
	{
		await WslcTest.SkipIfUnavailableAsync();

		string dir = Path.Combine(Path.GetTempPath(), "wslc-bind-" + Guid.NewGuid().ToString("N")[..6]);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "hello.txt"), "hello-bind\n");
		try
		{
			await using WslContainer container = new ContainerBuilder()
				.WithImage("alpine:latest")
				.WithBindMount(dir, "/mnt/data")
				.WithCommand("/bin/cat", "/mnt/data/hello.txt")
				.Build();

			await container.StartAsync();
			string logs = await container.GetLogsAsync();
			await Assert.That(logs).Contains("hello-bind");
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Test]
	public async Task NamedVolume_IsSharedBetweenContainers()
	{
		await WslcTest.SkipIfUnavailableAsync();

		string volume = "wslc-vol-" + Guid.NewGuid().ToString("N")[..6];
		await using WslContainer writer = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithVolumeMount(volume, "/data")
			.WithCommand("/bin/sh", "-c", "echo shared-data > /data/file.txt; sleep 300")
			.Build();
		await writer.StartAsync();
		await Task.Delay(1500);

		await using WslContainer reader = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithVolumeMount(volume, "/data")
			.WithCommand("/bin/cat", "/data/file.txt")
			.Build();
		await reader.StartAsync();
		string logs = await reader.GetLogsAsync();
		await Assert.That(logs).Contains("shared-data");
	}
}
