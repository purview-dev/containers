using System.Runtime.CompilerServices;
using Purview.Containers.Runtime;
using Purview.Containers.Waiting;
using TcContainer = DotNet.Testcontainers.Containers.IContainer;
using TcStates = DotNet.Testcontainers.Containers.TestcontainersStates;

namespace Purview.Containers.Docker;

/// <summary>
/// A Docker container adapted to the backend-neutral <see cref="IContainer" /> contract. Readiness uses
/// the shared wait strategies (host TCP/HTTP, in-container command, log match), so a module behaves the
/// same here as it does on WSLC.
/// </summary>
public sealed class DockerContainer : IContainer
{
	readonly IContainerConfiguration _configuration;
	int _disposed;

	internal DockerContainer(IContainerConfiguration configuration, TcContainer container, string name)
	{
		_configuration = configuration;
		Handle = container;
		Name = name;
	}

	/// <summary>The container Testcontainers created for this adapter.</summary>
	internal TcContainer Handle { get; }

	/// <inheritdoc />
	public string Id => Handle.Id;

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public ContainerState State => ToState(Handle.State);

	/// <inheritdoc />
	public string Image => _configuration.Image;

	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		await Handle.StartAsync(cancellationToken).ConfigureAwait(false);

		if (_configuration.WaitStrategies.Count > 0)
		{
			WaitContext context = new(this, GetMappedPublicPorts(), networkIp: null);
			await WaitStrategyRunner
				.RunAsync(context, _configuration.WaitStrategies, _configuration.StartupTimeout, cancellationToken)
				.ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		return Handle.StopAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<ExecResult> ExecAsync(
		string[] command,
		ExecOptions? options = null,
		CancellationToken cancellationToken = default
	)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		ArgumentNullException.ThrowIfNull(command);

		if (options?.EnableStandardInput == true)
		{
			throw new ContainerNotSupportedException(
				"ExecOptions.EnableStandardInput is not supported by the Docker backend."
			);
		}

		if (options?.Environment is { Count: > 0 })
		{
			throw new ContainerNotSupportedException(
				"ExecOptions.Environment is not supported by the Docker backend; set it on the container instead."
			);
		}

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (options?.Timeout is TimeSpan timeout)
		{
			timeoutSource.CancelAfter(timeout);
		}

		// Testcontainers exposes a single argv exec overload, so a requested working directory is applied
		// by executing through the shell.
		var argv = options?.WorkingDirectory is string workingDirectory
			? ["sh", "-c", $"cd {Quote(workingDirectory)} && {string.Join(' ', command.Select(Quote))}"]
			: command;

		var result = await Handle.ExecAsync(argv, timeoutSource.Token).ConfigureAwait(false);
		return new ExecResult(result.ExitCode ?? -1, result.Stdout, result.Stderr);
	}

	/// <inheritdoc />
	public ushort GetMappedPublicPort(ushort containerPort)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		return Handle.GetMappedPublicPort(containerPort);
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		Dictionary<ushort, ushort> mappings = [];
		foreach (var binding in _configuration.PortBindings)
		{
			mappings[binding.ContainerPort] = Handle.GetMappedPublicPort(binding.ContainerPort);
		}

		return mappings;
	}

	/// <inheritdoc />
	public string GetConnectionString(ConnectionMode connectionMode = ConnectionMode.Host)
	{
		if (connectionMode != ConnectionMode.Host)
		{
			throw new ConnectionStringModeNotSupportedException(connectionMode, GetType());
		}

		var first = GetMappedPublicPorts().FirstOrDefault();
		if (first.Key == 0 && first.Value == 0)
		{
			throw new ConnectionStringNotAvailableException(connectionMode, GetType());
		}

		return $"127.0.0.1:{first.Value}";
	}

	/// <inheritdoc />
	public string GetConnectionString(string name, ConnectionMode connectionMode = ConnectionMode.Host)
	{
		throw new ConnectionStringNameNotSupportedException(GetType(), name);
	}

	/// <inheritdoc />
	public async Task<string> GetLogsAsync(LogOutput? stream = null, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		var (stdout, stderr) = await ReadLogsAsync(cancellationToken).ConfigureAwait(false);
		return stream switch
		{
			LogOutput.Stdout => stdout,
			LogOutput.Stderr => stderr,
			_ => stdout + stderr,
		};
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<ContainerLogEntry> GetLogsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

		// Testcontainers exposes no positioned log stream, so the accumulated output is polled and new lines
		// are emitted until the container is no longer running.
		var emitted = 0;
		while (true)
		{
			var (stdout, stderr) = await ReadLogsAsync(cancellationToken).ConfigureAwait(false);
			var lines = Split(stdout, stderr);
			for (; emitted < lines.Count; emitted++)
			{
				yield return lines[emitted];
			}

			if (State is not ContainerState.Running)
			{
				yield break;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		await Handle.DisposeAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}

	Task<(string Stdout, string Stderr)> ReadLogsAsync(CancellationToken cancellationToken)
	{
		// Docker filters logs by a time window and compares it against UTC log timestamps, so both bounds are
		// UTC: the epoch (DateTime.MinValue is not a valid Docker timestamp) and now plus a small margin that
		// absorbs second-level truncation.
		return Handle.GetLogsAsync(
			DateTime.UnixEpoch,
			DateTime.UtcNow.AddMinutes(1),
			timestampsEnabled: false,
			cancellationToken
		);
	}

	static List<ContainerLogEntry> Split(string stdout, string stderr)
	{
		List<ContainerLogEntry> entries = [];
		Append(entries, stdout, LogOutput.Stdout);
		Append(entries, stderr, LogOutput.Stderr);
		return entries;
	}

	static void Append(List<ContainerLogEntry> entries, string content, LogOutput stream)
	{
		foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			entries.Add(new ContainerLogEntry(DateTimeOffset.Now, stream, line.TrimEnd('\r')));
		}
	}

	static string Quote(string value) =>
		value.Contains('\'', StringComparison.Ordinal)
			? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
			: $"'{value}'";

	static ContainerState ToState(TcStates state) =>
		state switch
		{
			TcStates.Created => ContainerState.Created,
			TcStates.Running or TcStates.Paused or TcStates.Restarting => ContainerState.Running,
			TcStates.Exited or TcStates.Dead => ContainerState.Exited,
			TcStates.Undefined or _ => ContainerState.Invalid,
		};
}
