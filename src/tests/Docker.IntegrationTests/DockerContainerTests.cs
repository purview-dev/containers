using Purview.Containers.Waiting;

namespace Purview.Containers.Docker;

/// <summary>
/// End-to-end coverage of the Docker backend against a real daemon. These tests are integration tests, so
/// the shared pipeline (filtered to <c>[Category=Unit]</c>) never runs them; they skip themselves when no
/// Docker daemon is reachable.
/// </summary>
public class DockerContainerTests
{
	const string Image = "alpine:3.19";

	static Task SkipIfUnavailableAsync() => DockerTest.SkipIfUnavailableAsync();

	static Container Build(ushort? mappedPort = null, IWaitStrategy? wait = null)
	{
		var builder = new ContainerBuilder().WithImage(Image).WithCommand("/bin/sh", "-c", "echo ready && sleep 60");

		if (mappedPort is ushort port)
		{
			builder = builder.WithPortBinding(port, assignRandomHostPort: true);
		}

		if (wait is not null)
		{
			builder = builder.WithWaitStrategy(wait);
		}

		return builder.Build();
	}

	static Container BuildWithMount(string source, bool readOnly) =>
		new ContainerBuilder()
			.WithBackend(DockerContainerBackend.Create())
			.WithImage(Image)
			.WithCommand("/bin/sh", "-c", "echo ready && sleep 60")
			.WithBindMount(source, "/mounted", readOnly)
			.Build();

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task Start_BindMount_ReadsHostFilesWithoutPathConfiguration(
		bool mountFile,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		await SkipIfUnavailableAsync();
		var directory = Path.Combine(Path.GetTempPath(), $"purview bind {Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		var file = Path.Combine(directory, "input.txt");

		try
		{
			await File.WriteAllTextAsync(file, "host-content", cancellationToken);
			await using var container = BuildWithMount(mountFile ? file : directory, readOnly: true);

			// Act
			await container.StartAsync(cancellationToken);
			var read = await container.ExecAsync(
				["cat", mountFile ? "/mounted" : "/mounted/input.txt"],
				cancellationToken: cancellationToken
			);
			var write = await container.ExecAsync(
				["sh", "-c", mountFile ? "echo changed > /mounted" : "echo changed > /mounted/input.txt"],
				cancellationToken: cancellationToken
			);

			// Assert
			await Assert.That(read.IsSuccess).IsTrue();
			await Assert.That(read.Stdout).Contains("host-content");
			await Assert.That(write.IsSuccess).IsFalse();
			await Assert.That(await File.ReadAllTextAsync(file, cancellationToken)).IsEqualTo("host-content");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task Start_WritableBindMount_WritesBackToTheHost(CancellationToken cancellationToken)
	{
		// Arrange
		await SkipIfUnavailableAsync();
		var directory = Path.Combine(Path.GetTempPath(), $"purview bind {Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);

		try
		{
			await using var container = BuildWithMount(directory, readOnly: false);

			// Act
			await container.StartAsync(cancellationToken);
			var write = await container.ExecAsync(
				["sh", "-c", "printf container-content > /mounted/output.txt"],
				cancellationToken: cancellationToken
			);

			// Assert
			await Assert.That(write.IsSuccess).IsTrue();
			await Assert
				.That(await File.ReadAllTextAsync(Path.Combine(directory, "output.txt"), cancellationToken))
				.IsEqualTo("container-content");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task GetInfoAsync_ReportsTheDockerServerVersion()
	{
		await SkipIfUnavailableAsync();

		var info = await DockerContainerBackend.Create().GetInfoAsync();

		await Assert.That(info.Name).IsEqualTo("docker");
		await Assert.That(info.IsUsable).IsTrue();
		await Assert.That(info.Version).IsNotEmpty();
	}

	[Test]
	public async Task Start_ExecAndLogs_RoundTrip()
	{
		await SkipIfUnavailableAsync();

		await using var container = Build();
		await container.StartAsync();

		await Assert.That(container.State).IsEqualTo(ContainerState.Running);
		await Assert.That(container.Id).IsNotEmpty();

		var executed = await container.ExecAsync(["/bin/echo", "hello-from-docker"]);
		await Assert.That(executed.IsSuccess).IsTrue();
		await Assert.That(executed.Stdout).Contains("hello-from-docker");

		var logs = await container.GetLogsAsync();
		await Assert.That(logs).Contains("ready");

		await container.StopAsync();

		await Assert.That(container.State).IsEqualTo(ContainerState.Exited);
	}

	[Test]
	public async Task Start_MapsRandomHostPorts()
	{
		await SkipIfUnavailableAsync();

		await using var container = Build(mappedPort: 8080);
		await container.StartAsync();

		var hostPort = container.GetMappedPublicPort(8080);

		await Assert.That(hostPort).IsGreaterThan((ushort)0);
		await Assert.That(container.GetMappedPublicPorts()[8080]).IsEqualTo(hostPort);
	}

	[Test]
	public async Task Start_HonoursTheSharedWaitStrategy()
	{
		await SkipIfUnavailableAsync();

		await using var container = Build(wait: Wait.ForLogMessage("ready"));
		await container.StartAsync();

		await Assert.That(container.State).IsEqualTo(ContainerState.Running);
	}

	[Test]
	public async Task DisposeAsync_IsIdempotent()
	{
		await SkipIfUnavailableAsync();

		var container = Build();
		await container.StartAsync();
		await container.DisposeAsync();
		await container.DisposeAsync();

		await Assert.That(container.State).IsNotEqualTo(ContainerState.Running);
	}
}
