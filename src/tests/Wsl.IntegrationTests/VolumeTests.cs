using Purview.Containers.Waiting;

namespace Purview.Containers.Wsl;

[Explicit]
public class VolumeTests
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
			.WithWaitStrategy(Wait.ForCommand("/bin/sh", "-c", "test -f /data/file.txt"))
			.Build();
		await writer.StartAsync(cancellationToken);

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

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SessionCleanup_DeletesNamedVolumesOnTerminate(bool useCli, CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		var storagePath = Path.Combine(Path.GetTempPath(), "wslc-session-cleanup-" + Guid.NewGuid().ToString("N")[..8]);
		var volume = "wslc-clean-" + Guid.NewGuid().ToString("N")[..6];
		try
		{
			// The writer is deliberately not disposed: session cleanup owns the container and its volume.
			await using (var runtime = CreateRuntime(storagePath, cleanUpVolumes: true))
			{
				var writerBuilder = new ContainerBuilder(new WslContainerBackend(runtime))
					.WithImage("alpine:latest")
					.WithVolumeMount(volume, "/data")
					.WithCommand("/bin/sh", "-c", "echo cleanup-data > /data/file.txt; sleep 300")
					.WithWaitStrategy(Wait.ForCommand("/bin/sh", "-c", "test -f /data/file.txt"));
				if (useCli)
				{
					// A label forces the CLI creation path, which records volumes separately.
					writerBuilder = writerBuilder.WithLabel("purview.test", "session-cleanup");
				}

				var writer = writerBuilder.Build();
				await writer.StartAsync(cancellationToken);
			}

			await using (var runtime = CreateRuntime(storagePath, cleanUpVolumes: true))
			{
				await using var reader = new ContainerBuilder(new WslContainerBackend(runtime))
					.WithImage("alpine:latest")
					.WithVolumeMount(volume, "/data")
					.WithCommand("/bin/sh", "-c", "test ! -f /data/file.txt; sleep 300")
					.WithWaitStrategy(Wait.ForCommand("/bin/sh", "-c", "test ! -f /data/file.txt"))
					.Build();
				await reader.StartAsync(cancellationToken);
			}
		}
		finally
		{
			DeleteStorageBestEffort(storagePath);
		}
	}

	[Test]
	public async Task SessionCleanup_Disabled_RetainsNamedVolumesAcrossRuntimes(CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		var storagePath = Path.Combine(Path.GetTempPath(), "wslc-session-retain-" + Guid.NewGuid().ToString("N")[..8]);
		var volume = "wslc-retain-" + Guid.NewGuid().ToString("N")[..6];
		try
		{
			await using (var runtime = CreateRuntime(storagePath, cleanUpVolumes: false))
			{
				var writer = new ContainerBuilder(new WslContainerBackend(runtime))
					.WithImage("alpine:latest")
					.WithVolumeMount(volume, "/data")
					.WithCommand("/bin/sh", "-c", "echo retained-data > /data/file.txt; sleep 300")
					.WithWaitStrategy(Wait.ForCommand("/bin/sh", "-c", "test -f /data/file.txt"))
					.Build();
				await writer.StartAsync(cancellationToken);
			}

			await using (var runtime = CreateRuntime(storagePath, cleanUpVolumes: false))
			{
				await using var reader = new ContainerBuilder(new WslContainerBackend(runtime))
					.WithImage("alpine:latest")
					.WithVolumeMount(volume, "/data")
					.WithCommand("/bin/sh", "-c", "test -f /data/file.txt; sleep 300")
					.WithWaitStrategy(Wait.ForCommand("/bin/sh", "-c", "test -f /data/file.txt"))
					.Build();
				await reader.StartAsync(cancellationToken);
			}
		}
		finally
		{
			DeleteStorageBestEffort(storagePath);
		}
	}

	[Test]
	public async Task ExplicitBackendCleanup_DeletesVolumesEvenWhenAutomaticCleanupIsOff(
		CancellationToken cancellationToken
	)
	{
		await WslcTest.SkipIfUnavailableAsync();

		var storagePath = Path.Combine(
			Path.GetTempPath(),
			"wslc-explicit-cleanup-" + Guid.NewGuid().ToString("N")[..8]
		);
		var volume = "wslc-explicit-" + Guid.NewGuid().ToString("N")[..6];
		try
		{
			await using var runtime = CreateRuntime(storagePath, cleanUpVolumes: false);
			WslContainerBackend backend = new(runtime);
			var writer = new ContainerBuilder(backend)
				.WithImage("alpine:latest")
				.WithVolumeMount(volume, "/data")
				.WithCommand("/bin/sh", "-c", "echo explicit-data > /data/file.txt; sleep 300")
				.WithWaitStrategy(Wait.ForCommand("/bin/sh", "-c", "test -f /data/file.txt"))
				.Build();
			await writer.StartAsync(cancellationToken);

			// The generic explicit cleanup does not depend on the automatic session switch.
			await backend.CleanUpAsync(cancellationToken);
			await writer.DisposeAsync();

			await using var reader = new ContainerBuilder(backend)
				.WithImage("alpine:latest")
				.WithVolumeMount(volume, "/data")
				.WithCommand("/bin/sh", "-c", "test ! -f /data/file.txt; sleep 300")
				.WithWaitStrategy(Wait.ForCommand("/bin/sh", "-c", "test ! -f /data/file.txt"))
				.Build();
			await reader.StartAsync(cancellationToken);
		}
		finally
		{
			DeleteStorageBestEffort(storagePath);
		}
	}

	static WslContainerRuntime CreateRuntime(string storagePath, bool? cleanUpVolumes) =>
		new(
			new WslContainerRuntimeOptions
			{
				StoragePath = storagePath,
				CleanUpVolumesOnTerminate = cleanUpVolumes,
				DisableProcessExitCleanup = true,
			}
		);

	static void DeleteStorageBestEffort(string path)
	{
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// The session manager may still hold the store VHD briefly.
		}
		catch (UnauthorizedAccessException)
		{
			// Same.
		}
	}
}
