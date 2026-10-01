namespace Purview.Containers;

/// <summary>A throwaway Linux container running on WSL Containers.</summary>
public interface IContainer : IAsyncDisposable
{
	/// <summary>Container identifier, available after start.</summary>
	string Id { get; }

	/// <summary>Container name.</summary>
	string Name { get; }

	/// <summary>Current container state.</summary>
	ContainerState State { get; }

	/// <summary>The image the container was created from.</summary>
	string Image { get; }

	/// <summary>Starts the container: ensures the image, creates and starts the init process.</summary>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>Stops the container (SIGTERM then SIGKILL). Idempotent.</summary>
	Task StopAsync(CancellationToken cancellationToken = default);

	/// <summary>Runs a command inside the running container without a shell.</summary>
	Task<ExecResult> ExecAsync(
		string[] command,
		ExecOptions? options = null,
		CancellationToken cancellationToken = default
	);

	/// <summary>Returns the host port mapped to the given container port.</summary>
	ushort GetMappedPublicPort(ushort containerPort);

	/// <summary>Returns all container-port → host-port mappings.</summary>
	IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts();

	/// <summary>Returns recently captured init-process output (stdout, stderr, or combined).</summary>
	Task<string> GetLogsAsync(LogOutput? stream = null, CancellationToken cancellationToken = default);

	/// <summary>Tails init-process output as it is produced.</summary>
	IAsyncEnumerable<ContainerLogEntry> GetLogsAsync(CancellationToken cancellationToken);
}
