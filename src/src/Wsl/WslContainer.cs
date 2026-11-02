using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.WSL.Containers;
using Purview.Containers.Waiting;
using MsContainer = Microsoft.WSL.Containers.Container;
using MsContainerState = Microsoft.WSL.Containers.ContainerState;
using MsProcess = Microsoft.WSL.Containers.Process;
using MsSignal = Microsoft.WSL.Containers.Signal;

namespace Purview.Containers.Wsl;

/// <summary>A WSLC-backed throwaway container.</summary>
[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Stop/delete/exec teardown is best-effort; WSLC surfaces a wide range of COMException variants."
)]
public class WslContainer : IContainer
{
	readonly LogBuffer _logBuffer = new();
	WslContainerSession? _session;
	MsContainer? _handle;
	IReadOnlyDictionary<ushort, ushort> _portMappings = new Dictionary<ushort, ushort>();
	string? _networkIp;
	int _started;
	int _disposed;
	ContainerState _state;
	bool _usesCLI;

	/// <summary>Creates a container bound to a runtime. Prefer building via a <see cref="ContainerBuilder" />.</summary>
	public WslContainer(ContainerConfiguration configuration, IContainerRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(runtime);
		Configuration = configuration;
		Runtime = runtime;
		Name = configuration.Name ?? GenerateName(configuration);
	}

	/// <summary>The immutable configuration this container was built from.</summary>
	protected ContainerConfiguration Configuration { get; }

	/// <summary>The runtime this container is bound to.</summary>
	protected IContainerRuntime Runtime { get; }

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public string Id => _handle?.Id ?? string.Empty;

	/// <inheritdoc />
	public ContainerState State => _handle is { } handle ? MapState(handle.State) : _state;

	/// <inheritdoc />
	public string Image => Configuration.Image;

	/// <inheritdoc />
	public string? NetworkIp => _networkIp;

	/// <inheritdoc />
	public virtual async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		cancellationToken.ThrowIfCancellationRequested();
		if (Volatile.Read(ref _started) == 1)
		{
			return;
		}

		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Configuration.StartupTimeout);
		var token = deadline.Token;
		try
		{
			var sessionLocal = (WslContainerSession)await Runtime.GetSessionAsync(token).ConfigureAwait(false);
			_session = sessionLocal;
			await _session
				.EnsureImageAsync(
					Configuration.Image,
					Configuration.PullPolicy,
					progress: null,
					Configuration.RegistryCredentials,
					token
				)
				.ConfigureAwait(false);

			_usesCLI =
				Configuration.Entrypoint.Count > 0
				|| Configuration.User is not null
				|| Configuration.Labels.Count > 0
				|| Configuration.Network is not null
				|| Configuration.Reuse;
			var container = _usesCLI
				? await _session.CreateConfiguredContainerAsync(Configuration, Name, token).ConfigureAwait(false)
				: await _session
					.CreateContainerAsync(WslSettingsMapper.ToContainerSettings(Configuration, Name), token)
					.ConfigureAwait(false);
			_handle = container;
			_logBuffer.Attach(container.InitProcess);
			if (container.State == MsContainerState.Created)
			{
				foreach (var mapping in Configuration.ResourceMappings)
					await WslFileCopy
						.CopyAsync(_session, container.Id, mapping.Source, mapping.Target, token)
						.ConfigureAwait(false);
				await _session.StartContainerAsync(container, token).ConfigureAwait(false);
			}

			_state = MapState(container.State);
			_portMappings = WslInspectParser.ParseHostPorts(container.Inspect());
			_networkIp = WslInspectParser.TryGetNetworkIp(container.Inspect(), Configuration.Network ?? "bridge");
			Volatile.Write(ref _started, 1);

			if (Configuration.WaitStrategies.Count > 0)
			{
				WaitContext waitContext = new(this, _portMappings, _networkIp);
				await WaitStrategyRunner
					.RunAsync(waitContext, Configuration.WaitStrategies, Configuration.StartupTimeout, token)
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
	public virtual async Task StopAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Configuration.StopTimeout);
		deadline.Token.ThrowIfCancellationRequested();
		if (_handle is null || _session is null)
		{
			return;
		}

		await _session
			.StopContainerAsync(
				_handle,
				MsSignal.SIGTERM,
				TimeSpan.FromSeconds(Math.Min(10, Configuration.StopTimeout.TotalSeconds * 2 / 3)),
				deadline.Token
			)
			.ConfigureAwait(false);

		_state = MapState(_handle.State);
	}

	/// <inheritdoc />
	public virtual async Task<ExecResult> ExecAsync(
		string[] command,
		ExecOptions? options = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(command);
		if (command.Length == 0)
		{
			throw new ArgumentException("Command cannot be empty.", nameof(command));
		}

		EnsureStarted();
		if (options?.EnableStandardInput == true)
			throw new Runtime.ContainerNotSupportedException("Exec capture does not expose standard input.");
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(options?.Timeout ?? Configuration.ExecTimeout);
		var token = deadline.Token;
		token.ThrowIfCancellationRequested();
		if (_handle is null || _session is null)
		{
			throw new InvalidOperationException("Container is not started.");
		}

		ProcessSettings settings = new()
		{
			CommandLine = [.. command],
			OutputMode = ProcessOutputMode.Event,
			WorkingDirectory = options?.WorkingDirectory,
		};
		if (options?.Environment is { Count: > 0 })
		{
			settings.EnvironmentVariables = new Dictionary<string, string>(options.Environment);
		}

		using var process = await _session.CreateProcessAsync(_handle, settings, token).ConfigureAwait(false);
		StringBuilder stdout = new();
		StringBuilder stderr = new();
		Lock outputLock = new();
		TaskCompletionSource<int> exitSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		process.OutputReceived += data =>
		{
			lock (outputLock)
				stdout.Append(Encoding.UTF8.GetString(data));
		};
		process.ErrorReceived += data =>
		{
			lock (outputLock)
				stderr.Append(Encoding.UTF8.GetString(data));
		};
		process.Exited += code => exitSource.TrySetResult(code);

		try
		{
			await _session.StartProcessAsync(process, token).ConfigureAwait(false);
			// State can report exit before output callbacks drain; the exit event follows output delivery.
			var exitCode = await exitSource.Task.WaitAsync(token).ConfigureAwait(false);
			lock (outputLock)
				return new ExecResult(exitCode, stdout.ToString(), stderr.ToString());
		}
		catch
		{
			TryKill(process);
			throw;
		}
	}

	static void TryKill(MsProcess process)
	{
		try
		{
			process.Signal(MsSignal.SIGKILL);
		}
		catch { }
	}

	/// <inheritdoc />
	public virtual ushort GetMappedPublicPort(ushort containerPort)
	{
		EnsureStarted();
		if (!_portMappings.TryGetValue(containerPort, out var hostPort))
		{
			throw new WslContainerException($"No host port mapping found for container port {containerPort}.");
		}

		// WSLC does not support dynamic host port assignment; the host port is always the same as the container port.
		return hostPort;
	}

	/// <inheritdoc />
	public virtual IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts()
	{
		EnsureStarted();
		return _portMappings;
	}

	/// <inheritdoc />
	public string GetConnectionString(ConnectionMode connectionMode = ConnectionMode.Host)
	{
		EnsureStarted();
		if (connectionMode != ConnectionMode.Host)
		{
			throw new ConnectionStringModeNotSupportedException(connectionMode, GetType());
		}

		var first = _portMappings.FirstOrDefault();
		if (first.Key == 0 && first.Value == 0)
		{
			throw new ConnectionStringNotAvailableException(connectionMode, GetType());
		}

		// WSLC does not support dynamic host port assignment; the host port is always the same as the container port.
		return $"127.0.0.1:{first.Value}";
	}

	/// <inheritdoc />
	public string GetConnectionString(string name, ConnectionMode connectionMode = ConnectionMode.Host)
	{
		EnsureStarted();
		throw new ConnectionStringNameNotSupportedException(GetType(), name);
	}

	/// <inheritdoc />
	public virtual async Task<string> GetLogsAsync(
		LogOutput? stream = null,
		CancellationToken cancellationToken = default
	)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		cancellationToken.ThrowIfCancellationRequested();
		if (_usesCLI && _session is not null && _handle is not null)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(Configuration.OperationTimeout);
			var result = await WslCommand.RunAsync(_session.Name, ["logs", Id], deadline.Token).ConfigureAwait(false);
			var stdout = Encoding.UTF8.GetString(result.Output);
			return stream switch
			{
				LogOutput.Stdout => stdout,
				LogOutput.Stderr => result.Error,
				_ => stdout + result.Error,
			};
		}
		return _logBuffer.GetLogs(stream);
	}

	/// <inheritdoc />
	public virtual async IAsyncEnumerable<ContainerLogEntry> GetLogsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		if (!_usesCLI)
		{
			await foreach (var entry in _logBuffer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
				yield return entry;
			yield break;
		}
		var stdoutEmitted = 0;
		var stderrEmitted = 0;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var exited = State != ContainerState.Running;
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(Configuration.OperationTimeout);
			var logs = await WslCommand.RunAsync(_session!.Name, ["logs", Id], deadline.Token).ConfigureAwait(false);
			foreach (
				var entry in NewLogEntries(
					Encoding.UTF8.GetString(logs.Output),
					LogOutput.Stdout,
					ref stdoutEmitted,
					exited
				)
			)
				yield return entry;
			foreach (var entry in NewLogEntries(logs.Error, LogOutput.Stderr, ref stderrEmitted, exited))
				yield return entry;
			if (exited)
				yield break;
			await Task.Delay(100, cancellationToken).ConfigureAwait(false);
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
	public virtual async Task CopyAsync(string source, string target, CancellationToken cancellationToken = default)
	{
		EnsureStarted();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Configuration.OperationTimeout);
		await WslFileCopy.CopyAsync(_session!, Id, source, target, deadline.Token).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public virtual async Task DeleteAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Configuration.CleanupTimeout);
		deadline.Token.ThrowIfCancellationRequested();
		if (_handle is not null && _session is not null)
		{
			await _session
				.DeleteContainerAsync(_handle, DeleteContainerOption.Force, deadline.Token)
				.ConfigureAwait(false);
			_handle.Dispose();
			_handle = null;
			_state = ContainerState.Deleted;
			_logBuffer.Complete();
		}
	}

	/// <inheritdoc />
	public virtual async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		EnsureStarted();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Configuration.OperationTimeout);
		return await WslFileCopy.ReadAsync(_session!, Id, path, deadline.Token).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public virtual async Task<long> WaitForExitAsync(CancellationToken cancellationToken = default)
	{
		EnsureStarted();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Configuration.ExecTimeout);
		while (State == ContainerState.Running)
			await Task.Delay(100, deadline.Token).ConfigureAwait(false);
		deadline.Token.ThrowIfCancellationRequested();
		return _handle!.InitProcess.ExitCode;
	}

	/// <inheritdoc />
	public virtual async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		try
		{
			if (_handle is not null && _session is not null)
			{
				_state = MapState(_handle.State);
				if (Configuration.Reuse)
				{
					_logBuffer.Detach();
					_handle.Dispose();
					return;
				}
				using CancellationTokenSource cleanup = new(Configuration.CleanupTimeout);
				try
				{
					if (_handle.State == MsContainerState.Running)
					{
						await _session
							.StopContainerAsync(
								_handle,
								MsSignal.SIGTERM,
								TimeSpan.FromSeconds(Math.Min(10, Configuration.StopTimeout.TotalSeconds * 2 / 3)),
								cleanup.Token
							)
							.ConfigureAwait(false);
					}
				}
				catch { }

				try
				{
					_state = MapState(_handle.State);
					await _session
						.DeleteContainerAsync(_handle, DeleteContainerOption.Force, cleanup.Token)
						.ConfigureAwait(false);
				}
				catch { }

				try
				{
					_handle.Dispose();
				}
				catch { }
			}
		}
		finally
		{
			_logBuffer.Detach();
			_logBuffer.Complete();
			_handle = null;
			GC.SuppressFinalize(this);
		}
	}

	void EnsureStarted()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		if (Volatile.Read(ref _started) == 0)
		{
			throw new InvalidOperationException("Container has not been started. Call StartAsync() first.");
		}
	}

	static ContainerState MapState(MsContainerState state)
	{
		return (ContainerState)state;
	}

	static string GenerateName(ContainerConfiguration configuration)
	{
		string shortName;
		try
		{
			shortName = Images.Image.Parse(configuration.Image).ShortName;
		}
		catch
		{
			shortName = "container";
		}

		string safe = new([
			.. shortName.Select(character =>
				char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-'
			),
		]);
		return $"{safe}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..6]}";
	}
}
