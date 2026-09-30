namespace Purview.WslContainers;

class VolumeTests
{
	[Test]
	public async Task BindMount_ReadsHostFile(CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		var dir = Path.Combine(Path.GetTempPath(), "wslc-bind-" + Guid.NewGuid().ToString("N")[..6]);
		Directory.CreateDirectory(dir);
		await File.WriteAllTextAsync(Path.Combine(dir, "hello.txt"), "hello-bind\n", cancellationToken);
		try
		{
			await using var container = new ContainerBuilder()
				.WithImage("alpine:latest")
				.WithBindMount(dir, "/mnt/data")
				.WithCommand("/bin/cat", "/mnt/data/hello.txt")
				.Build();

			await container.StartAsync(cancellationToken);

			// The live log stream completes when the init process exits; bound it so a regression fails
			// instead of hanging the suite.
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(60));
			List<string> logs = [];
			await foreach (var log in container.GetLogsAsync(timeout.Token))
			{
				logs.Add(log.Text);
			}

			await Assert.That(logs.Any(text => text.Contains("hello-bind", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Test]
	public async Task NamedVolume_IsSharedBetweenContainers(CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		var volume = "wslc-vol-" + Guid.NewGuid().ToString("N")[..6];
		await using var writer = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithVolumeMount(volume, "/data")
			.WithCommand("/bin/sh", "-c", "echo shared-data > /data/file.txt; sleep 300")
			.Build();
		await writer.StartAsync(cancellationToken);
		await Task.Delay(1500, cancellationToken);

		await using var reader = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithVolumeMount(volume, "/data")
			.WithCommand("/bin/cat", "/data/file.txt")
			.Build();
		await reader.StartAsync(cancellationToken);

		// The live log stream completes when the init process exits; bound it so a regression fails
		// instead of hanging the suite.
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(60));
		List<string> logs = [];
		await foreach (var log in reader.GetLogsAsync(timeout.Token))
		{
			logs.Add(log.Text);
		}

		await Assert.That(logs.Any(text => text.Contains("shared-data", StringComparison.Ordinal))).IsTrue();
	}
}
