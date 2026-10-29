using System.Text;
using Purview.Containers.Waiting;
#if DOCKER_BACKEND
using Backend = Purview.Containers.Docker.DockerContainerBackend;
#else
using Backend = Purview.Containers.Wsl.WslContainerBackend;
#endif

namespace Purview.Containers;

#if !DOCKER_BACKEND
[Explicit]
#endif
[NotInParallel]
public class BackendContractTests
{
	static Backend CreateBackend() => new();

	static Task EnsureAvailableAsync() =>
#if DOCKER_BACKEND
		Docker.DockerTest.SkipIfUnavailableAsync();
#else
		WslcTest.SkipIfUnavailableAsync();
#endif

	static Container CreateContainer(IContainerBackend backend, Action<ContainerBuilder>? configure = null)
	{
		var builder = new ContainerBuilder(backend).WithImage("alpine:latest").WithCommand("/bin/sleep", "300");
		configure?.Invoke(builder);
		return builder.Build();
	}

	[Test]
	public async Task StartAsync_EntrypointUserAndExecOverrides_PreserveArguments(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		await using var container = CreateContainer(
			CreateBackend(),
			builder =>
				builder
					.WithEntrypoint("/bin/sh")
					.WithCommand("-c", "echo ready; sleep 300")
					.WithUser("1000:1000")
					.WithLabel("purview.test", "metadata")
					.WithWaitStrategy(Wait.ForLogMessage("ready"))
		);
		await container.StartAsync(cancellationToken);
		var directory = "/tmp/a'b$HOME";
		await container.ExecAsync(["mkdir", directory], cancellationToken: cancellationToken);

		// Act
		var result = await container.ExecAsync(
			["sh", "-c", "id -u; printf '%s\\n' \"$VALUE\"; pwd"],
			new ExecOptions
			{
				WorkingDirectory = directory,
				Environment = new Dictionary<string, string> { ["VALUE"] = "literal '$HOME;$(echo wrong)" },
			},
			cancellationToken
		);

		// Assert
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stdout).Contains("1000\nliteral '$HOME;$(echo wrong)\n" + directory);
	}

	[Test]
	public async Task CopyAsync_PreStartAndRuntimeMappings_PreserveFileBytes(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		var directory = Path.Combine(Path.GetTempPath(), $"purview-copy-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var source = Path.Combine(directory, "input.bin");
			var emptyDirectory = Directory.CreateDirectory(Path.Combine(directory, "empty")).FullName;
			byte[] content = [0, 1, 2, 255, 13, 10, 128];
			await File.WriteAllBytesAsync(source, content, cancellationToken);
			await using var container = CreateContainer(
				CreateBackend(),
				builder =>
					builder
						.WithResourceMapping(source, "/tmp/pre-start.bin")
						.WithResourceMapping(directory, "/tmp/pre-start-dir")
						.WithCommand(
							"/bin/sh",
							"-c",
							"test -f /tmp/pre-start.bin && test -f /tmp/pre-start-dir/input.bin && test -d /tmp/pre-start-dir/empty && echo ready; sleep 300"
						)
						.WithWaitStrategy(Wait.ForLogMessage("ready"))
			);

			// Act
			await container.StartAsync(cancellationToken);
			var initial = await container.ReadFileAsync("/tmp/pre-start.bin", cancellationToken);
			await container.CopyAsync(directory, "/tmp/copied", cancellationToken);
			var copied = await container.ReadFileAsync("/tmp/copied/input.bin", cancellationToken);
			await container.CopyAsync(source, "/tmp/runtime.bin", cancellationToken);
			var runtime = await container.ReadFileAsync("/tmp/runtime.bin", cancellationToken);
			await container.CopyAsync(emptyDirectory, "/tmp/empty-copy", cancellationToken);
			var directories = await container.ExecAsync(
				["sh", "-c", "test -d /tmp/empty-copy && test -d /tmp/copied/empty"],
				cancellationToken: cancellationToken
			);

			// Assert
			await Assert.That(initial).IsEquivalentTo(content);
			await Assert.That(copied).IsEquivalentTo(content);
			await Assert.That(runtime).IsEquivalentTo(content);
			await Assert.That(directories.ExitCode).IsEqualTo(0);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task StartAsync_OneShotExitWait_ReturnsExpectedExitCode(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		await using var container = CreateContainer(
			CreateBackend(),
			builder =>
				builder
					.WithEntrypoint("/bin/sh")
					.WithCommand("-c", "sleep 1; echo complete; exit 7")
					.WithWaitStrategy(Wait.ForExitCode(7))
		);

		// Act
		await container.StartAsync(cancellationToken);
		var exitCode = await container.WaitForExitAsync(cancellationToken);

		// Assert
		await Assert.That(exitCode).IsEqualTo(7L);
		await Assert.That(container.State).IsEqualTo(ContainerState.Exited);
	}

	[Test]
	public async Task GetLogsAsync_PartialLinesAndStderr_AreDrainedOnExit(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		await using var container = CreateContainer(
			CreateBackend(),
			builder =>
				builder
					.WithEntrypoint("/bin/sh")
					.WithCommand(
						"-c",
						"printf first; sleep 1; printf ' line\\n'; printf 'problem\\n' >&2; printf final"
					)
		);
		await container.StartAsync(cancellationToken);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(10));
		List<ContainerLogEntry> entries = [];

		// Act
		await foreach (var entry in container.GetLogsAsync(deadline.Token))
			entries.Add(entry);

		// Assert
		await Assert
			.That(entries.Where(entry => entry.Stream == LogOutput.Stdout).Select(entry => entry.Text))
			.IsEquivalentTo(["first line", "final"]);
		await Assert
			.That(entries.Where(entry => entry.Stream == LogOutput.Stderr).Select(entry => entry.Text))
			.IsEquivalentTo(["problem"]);
	}

	[Test]
	public async Task StartAsync_OneShotUnexpectedExit_FailsWithoutReadinessRetries(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		await using var container = CreateContainer(
			CreateBackend(),
			builder =>
				builder.WithEntrypoint("/bin/sh").WithCommand("-c", "exit 2").WithWaitStrategy(Wait.ForExitCode())
		);

		// Act
		await Assert.That(() => container.StartAsync(cancellationToken)).Throws<Runtime.ContainerExitCodeException>();

		// Assert
		await Assert.That(container.State).IsNotEqualTo(ContainerState.Running);
	}

	[Test]
	public async Task CreateNetworkAsync_Aliases_ResolveBetweenContainers(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		var backend = CreateBackend();
		await using var network = await backend.CreateNetworkAsync(cancellationToken: cancellationToken);
		await using var server = CreateContainer(
			backend,
			builder => builder.WithNetwork(network.Name).WithNetworkAliases("peer")
		);
		await using var client = CreateContainer(backend, builder => builder.WithNetwork(network.Name));
		await server.StartAsync(cancellationToken);
		await client.StartAsync(cancellationToken);

		// Act
		var result = await client.ExecAsync(
			["ping", "-c", "1", "-W", "3", "peer"],
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result.ExitCode).IsEqualTo(0);
	}

	[Test]
	public async Task BuildImageAsync_Dockerfile_RunsOnSelectedBackend(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		var directory = Path.Combine(Path.GetTempPath(), $"purview-build-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			await File.WriteAllTextAsync(
				Path.Combine(directory, "Dockerfile"),
				"FROM alpine:latest\nRUN echo built > /probe\nCMD [\"echo image-default\"]\n",
				cancellationToken
			);
			var backend = CreateBackend();

			// Act
			await using var image = await backend.BuildImageAsync(
				new ImageBuildConfiguration
				{
					ContextDirectory = directory,
					Name = $"purview-contract:{Guid.NewGuid():N}",
				},
				cancellationToken
			);
			await using var container = CreateContainer(backend, builder => builder.WithImage(image.Name));
			await container.StartAsync(cancellationToken);
			var bytes = await container.ReadFileAsync("/probe", cancellationToken);
			await using var job = CreateContainer(
				backend,
				builder =>
					builder
						.WithImage(image.Name)
						.WithEntrypoint("/bin/sh", "-c")
						.WithCommand("echo explicit-command")
						.WithWaitStrategy(Wait.ForExitCode())
			);
			await job.StartAsync(cancellationToken);
			var logs = await job.GetLogsAsync(stream: null, cancellationToken: cancellationToken);

			// Assert
			await Assert.That(Encoding.UTF8.GetString(bytes).Trim()).IsEqualTo("built");
			await Assert.That(logs).Contains("explicit-command");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task StartAsync_Reuse_RetainsIdentityUntilExplicitDeletion(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		var backend = CreateBackend();
		var name = $"purview-reuse-{Guid.NewGuid():N}";
		var first = CreateContainer(backend, builder => builder.WithName(name).WithReuse());
		await first.StartAsync(cancellationToken);
		var id = first.Id;
		await first.StopAsync(cancellationToken);
		await first.DisposeAsync();
		await using var second = CreateContainer(backend, builder => builder.WithName(name).WithReuse());
		// Act
		await second.StartAsync(cancellationToken);
		try
		{
			// Assert
			await Assert.That(second.Id).IsEqualTo(id);
			await Assert.That(second.State).IsEqualTo(ContainerState.Running);
		}
		finally
		{
			await second.DeleteAsync(cancellationToken);
		}
	}

	[Test]
	public async Task StartAsync_NameCollision_DoesNotDeleteExistingContainer(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		var backend = CreateBackend();
		var name = $"purview-collision-{Guid.NewGuid():N}";
		await using var first = CreateContainer(
			backend,
			builder => builder.WithName(name).WithLabel("suite", "collision")
		);
		await first.StartAsync(cancellationToken);
		await using var second = CreateContainer(
			backend,
			builder => builder.WithName(name).WithLabel("suite", "collision")
		);

		// Act
		await Assert.That(() => second.StartAsync(cancellationToken)).Throws<Exception>();
		var result = await first.ExecAsync(["echo", "retained"], cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.Stdout).Contains("retained");
	}

	[Test]
	public async Task ExecAsync_CancelledWhileRunning_ThrowsAndLeavesContainerUsable(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		await EnsureAvailableAsync();
		await using var container = CreateContainer(CreateBackend());
		await container.StartAsync(cancellationToken);
		using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancelled.CancelAfter(TimeSpan.FromMilliseconds(150));

		// Act
		await Assert
			.That(async () =>
			{
				await container.ExecAsync(["sleep", "60"], cancellationToken: cancelled.Token);
			})
			.Throws<OperationCanceledException>();
		var result = await container.ExecAsync(["echo", "still-usable"], cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.Stdout).Contains("still-usable");
	}

	[Test]
	public async Task StopAsync_PreCancelled_PropagatesCancellation(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		await using var container = CreateContainer(CreateBackend());
		await container.StartAsync(cancellationToken);
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		// Act
		await Assert.That(() => container.StopAsync(cancelled.Token)).Throws<OperationCanceledException>();
		await Assert
			.That(async () =>
			{
				await container.GetLogsAsync(null, cancelled.Token);
			})
			.Throws<OperationCanceledException>();
		await Assert
			.That(async () =>
			{
				await container.ReadFileAsync("/etc/os-release", cancelled.Token);
			})
			.Throws<OperationCanceledException>();

		// Assert
		await Assert.That(container.State).IsEqualTo(ContainerState.Running);
	}

	[Test]
	public async Task StartAsync_UncooperativeReadiness_IsBoundedAndCleansUp(CancellationToken cancellationToken)
	{
		// Arrange
		await EnsureAvailableAsync();
		TaskCompletionSource<bool> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
		await using var container = CreateContainer(
			CreateBackend(),
			builder =>
				builder
					.WithStartupTimeout(TimeSpan.FromSeconds(2))
					.WithWaitStrategy(Wait.ForCustom((_, _) => never.Task))
		);

		// Act
		await Assert.That(() => container.StartAsync(cancellationToken)).Throws<OperationCanceledException>();

		// Assert
		await Assert.That(container.State).IsNotEqualTo(ContainerState.Running);
	}
}
