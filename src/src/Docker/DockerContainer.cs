using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Configurations;
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
	readonly Func<CancellationToken, Task<TcContainer>> _create;
	TcContainer? _handle;
	int _disposed;
	bool _deleted;
	readonly CancellationTokenSource _lifetime = new();
	Task<long>? _exitTask;

	internal DockerContainer(
		IContainerConfiguration configuration,
		Func<CancellationToken, Task<TcContainer>> create,
		string name
	)
	{
		_configuration = configuration;
		_create = create;
		Name = name;
	}

	/// <summary>The container Testcontainers created for this adapter.</summary>
	internal TcContainer Handle => _handle ?? throw new InvalidOperationException("Container has not been started.");

	/// <inheritdoc />
	public string Id => _handle?.Id ?? string.Empty;

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public ContainerState State =>
		_deleted ? ContainerState.Deleted
		: _handle is null ? ContainerState.Created
		: _exitTask?.IsCompletedSuccessfully == true ? ContainerState.Exited
		: ToState(Handle.State);

	/// <inheritdoc />
	public string Image => _configuration.Image;

	/// <inheritdoc />
	public string? NetworkIp { get; private set; }

	/// <inheritdoc />
	[SuppressMessage(
		"Design",
		"CA1031",
		Justification = "Failed-start cleanup must preserve the original error or cancellation."
	)]
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_configuration.StartupTimeout);
		deadline.Token.ThrowIfCancellationRequested();
		try
		{
			_handle ??= await _create(deadline.Token).ConfigureAwait(false);
			await Handle.StartAsync(deadline.Token).ConfigureAwait(false);
			_exitTask = ObserveExitAsync();
			_ = _exitTask.ContinueWith(
				static task => _ = task.Exception,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default
			);

			// Testcontainers reports an unspecified address (or none) when the container has no network.
			NetworkIp = Handle.IpAddress is { Length: > 0 } ip && ip != "0.0.0.0" ? ip : null;

			if (_configuration.WaitStrategies.Count > 0)
			{
				WaitContext context = new(this, GetMappedPublicPorts(), NetworkIp);
				await WaitStrategyRunner
					.RunAsync(context, _configuration.WaitStrategies, _configuration.StartupTimeout, deadline.Token)
					.ConfigureAwait(false);
			}
		}
		catch
		{
			try
			{
				await DisposeAsync().ConfigureAwait(false);
			}
			catch { }
			throw;
		}
	}

	/// <inheritdoc />
	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_configuration.StopTimeout);
		deadline.Token.ThrowIfCancellationRequested();
		if (_handle is not null)
			await Handle.StopAsync(deadline.Token).ConfigureAwait(false);
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
		if (command.Length == 0)
			throw new ArgumentException("Command cannot be empty.", nameof(command));

		if (options?.EnableStandardInput == true)
		{
			throw new ContainerNotSupportedException(
				"ExecOptions.EnableStandardInput is not supported by the Docker backend."
			);
		}

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(options?.Timeout ?? _configuration.ExecTimeout);
		timeoutSource.Token.ThrowIfCancellationRequested();

		// Testcontainers' argv overload cannot supply exec settings and logs argv. Send environment
		// separately through its Docker client transport rather than embedding secrets in a shell command.
		using var client = TestcontainersSettings
			.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid())
			.Build();
		var created = await client
			.Exec.CreateContainerExecAsync(
				Handle.Id,
				new global::Docker.DotNet.Models.ContainerExecCreateParameters
				{
					Cmd = command,
					Env = options?.Environment?.Select(pair => $"{pair.Key}={pair.Value}").ToList() ?? [],
					WorkingDir = options?.WorkingDirectory ?? string.Empty,
					AttachStdout = true,
					AttachStderr = true,
				},
				timeoutSource.Token
			)
			.ConfigureAwait(false);
		using var stream = await client
			.Exec.StartContainerExecAsync(
				created.ID,
				new global::Docker.DotNet.Models.ContainerExecStartParameters(),
				timeoutSource.Token
			)
			.ConfigureAwait(false);
		var (stdout, stderr) = await stream.ReadOutputToEndAsync(timeoutSource.Token).ConfigureAwait(false);
		var inspect = await client
			.Exec.InspectContainerExecAsync(created.ID, timeoutSource.Token)
			.ConfigureAwait(false);
		while (inspect.Running)
		{
			await Task.Delay(50, timeoutSource.Token).ConfigureAwait(false);
			inspect = await client
				.Exec.InspectContainerExecAsync(created.ID, timeoutSource.Token)
				.ConfigureAwait(false);
		}
		return new ExecResult(inspect.ExitCode ?? -1, stdout, stderr);
	}

	/// <inheritdoc />
	public ushort GetMappedPublicPort(ushort containerPort)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		var binding = _configuration.PortBindings.FirstOrDefault(binding => binding.ContainerPort == containerPort);
		return Handle.GetMappedPublicPort(
			$"{containerPort}/{(binding?.Protocol == Networking.PortProtocol.Udp ? "udp" : "tcp")}"
		);
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		Dictionary<ushort, ushort> mappings = [];
		foreach (var binding in _configuration.PortBindings)
		{
			mappings[binding.ContainerPort] = Handle.GetMappedPublicPort(
				$"{binding.ContainerPort}/{(binding.Protocol == Networking.PortProtocol.Udp ? "udp" : "tcp")}"
			);
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

		// The Docker backend is always local, so the connection string is always localhost with the first mapped port.
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
		var emittedStdout = 0;
		var emittedStderr = 0;
		while (true)
		{
			var exited = State is not ContainerState.Running;
			var (stdout, stderr) = await ReadLogsAsync(cancellationToken).ConfigureAwait(false);
			foreach (var entry in NewLogEntries(stdout, LogOutput.Stdout, ref emittedStdout, exited))
				yield return entry;
			foreach (var entry in NewLogEntries(stderr, LogOutput.Stderr, ref emittedStderr, exited))
				yield return entry;

			if (exited)
			{
				yield break;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
		}
	}

	static ContainerLogEntry[] NewLogEntries(string content, LogOutput stream, ref int emitted, bool exited)
	{
		var lines = content.Split('\n');
		var completeCount = exited && lines[^1].Length > 0 ? lines.Length : lines.Length - 1;
		var entries = lines
			.Skip(emitted)
			.Take(Math.Max(0, completeCount - emitted))
			.Select(line => new ContainerLogEntry(DateTimeOffset.UtcNow, stream, line.TrimEnd('\r')))
			.ToArray();
		emitted = completeCount;
		return entries;
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		try
		{
			await _lifetime.CancelAsync().ConfigureAwait(false);
			if (_handle is not null)
				await Handle.DisposeAsync().AsTask().WaitAsync(_configuration.CleanupTimeout).ConfigureAwait(false);
			if (!_configuration.Reuse)
				_deleted = true;
		}
		finally
		{
			_lifetime.Dispose();
			GC.SuppressFinalize(this);
		}
	}

	/// <inheritdoc />
	public async Task CopyAsync(string source, string target, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_configuration.OperationTimeout);
		await DockerFileCopy.CopyAsync(Handle.Id, source, target, deadline.Token).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task DeleteAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_configuration.CleanupTimeout);
		deadline.Token.ThrowIfCancellationRequested();
		if (_handle is not null && !_deleted)
		{
			using var client = TestcontainersSettings
				.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid())
				.Build();
			try
			{
				await client
					.Containers.RemoveContainerAsync(
						Handle.Id,
						new global::Docker.DotNet.Models.ContainerRemoveParameters
						{
							Force = true,
							RemoveVolumes = true,
						},
						deadline.Token
					)
					.ConfigureAwait(false);
			}
			catch (global::Docker.DotNet.DockerApiException exception)
				when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { }
			_deleted = true;
		}
	}

	/// <inheritdoc />
	public async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_configuration.OperationTimeout);
		return await Handle.ReadFileAsync(path, deadline.Token).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<long> WaitForExitAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_configuration.ExecTimeout);
		return await (_exitTask ?? throw new InvalidOperationException("Container has not been started."))
			.WaitAsync(deadline.Token)
			.ConfigureAwait(false);
	}

	async Task<long> ObserveExitAsync()
	{
		using var client = TestcontainersSettings
			.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid())
			.Build();
		var result = await client.Containers.WaitContainerAsync(Handle.Id, _lifetime.Token).ConfigureAwait(false);
		return result.StatusCode;
	}

	async Task<(string Stdout, string Stderr)> ReadLogsAsync(CancellationToken cancellationToken)
	{
		// Docker filters logs by a time window and compares it against UTC log timestamps, so both bounds are
		// UTC: the epoch (DateTime.MinValue is not a valid Docker timestamp) and now plus a small margin that
		// absorbs second-level truncation.
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_configuration.OperationTimeout);
		return await Handle
			.GetLogsAsync(DateTime.UnixEpoch, DateTime.UtcNow.AddMinutes(1), timestampsEnabled: false, deadline.Token)
			.ConfigureAwait(false);
	}

	static ContainerState ToState(TcStates state) =>
		state switch
		{
			TcStates.Created => ContainerState.Created,
			TcStates.Running or TcStates.Paused or TcStates.Restarting => ContainerState.Running,
			TcStates.Exited or TcStates.Dead => ContainerState.Exited,
			TcStates.Undefined or _ => ContainerState.Invalid,
		};
}
