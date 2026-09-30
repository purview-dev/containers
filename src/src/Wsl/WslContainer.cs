using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.WSL.Containers;
using Purview.Containers.Waiting;
using MsContainer = Microsoft.WSL.Containers.Container;
using MsContainerState = Microsoft.WSL.Containers.ContainerState;
using MsProcess = Microsoft.WSL.Containers.Process;
using MsProcessState = Microsoft.WSL.Containers.ProcessState;
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
	public ContainerState State { get; private set; }

	/// <inheritdoc />
	public string Image => Configuration.Image;

	/// <inheritdoc />
	public virtual async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		if (Volatile.Read(ref _started) == 1)
		{
			return;
		}

		var sessionLocal = (WslContainerSession)await Runtime.GetSessionAsync(cancellationToken).ConfigureAwait(false);
		_session = sessionLocal;
		await _session
			.EnsureImageAsync(
				Configuration.Image,
				Configuration.PullPolicy,
				progress: null,
				Configuration.RegistryCredentials,
				cancellationToken
			)
			.ConfigureAwait(false);

		var settings = WslSettingsMapper.ToContainerSettings(Configuration, Name);
		var container = await _session.CreateContainerAsync(settings, cancellationToken).ConfigureAwait(false);
		_handle = container;
		_logBuffer.Attach(container.InitProcess);
		await _session.StartContainerAsync(container, cancellationToken).ConfigureAwait(false);

		State = MapState(container.State);
		_portMappings = WslInspectParser.ParseHostPorts(container.Inspect());
		_networkIp = WslInspectParser.TryGetNetworkIp(container.Inspect());
		Volatile.Write(ref _started, 1);

		if (Configuration.WaitStrategies.Count > 0)
		{
			WaitContext waitContext = new(this, _portMappings, _networkIp);
			await WaitStrategyRunner
				.RunAsync(waitContext, Configuration.WaitStrategies, Configuration.StartupTimeout, cancellationToken)
				.ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public virtual async Task StopAsync(CancellationToken cancellationToken = default)
	{
		EnsureStarted();
		if (_handle is null || _session is null)
		{
			return;
		}

		try
		{
			await _session
				.StopContainerAsync(_handle, MsSignal.SIGTERM, TimeSpan.FromSeconds(10), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (WslContainerSessionTerminatedException)
		{
			throw;
		}
		catch
		{
			// already stopped; idempotent by design
		}

		State = MapState(_handle.State);
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

		var process = await _session.CreateProcessAsync(_handle, settings, cancellationToken).ConfigureAwait(false);
		using var cancelRegistration = cancellationToken.Register(
			static state =>
			{
				if (state is MsProcess processToSignal)
				{
					try
					{
						processToSignal.Signal(MsSignal.SIGKILL);
					}
					catch { }
				}
			},
			process
		);

		StringBuilder stdout = new();
		StringBuilder stderr = new();
		TaskCompletionSource<int> exitSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		process.OutputReceived += data => stdout.Append(Encoding.UTF8.GetString(data));
		process.ErrorReceived += data => stderr.Append(Encoding.UTF8.GetString(data));
		process.Exited += code => exitSource.TrySetResult(code);

		await _session.StartProcessAsync(process, cancellationToken).ConfigureAwait(false);

		var exitTask = exitSource.Task;
		int exitCode;
		if (options?.Timeout is TimeSpan timeout)
		{
			var completed = await Task.WhenAny(exitTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
			if (completed == exitTask)
			{
				exitCode = await exitTask.ConfigureAwait(false);
			}
			else
			{
				TryKill(process);
				exitCode = await PollForExitAsync(process, exitTask).ConfigureAwait(false);
			}
		}
		else
		{
			try
			{
				exitCode = await exitTask.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				TryKill(process);
				exitCode = await PollForExitAsync(process, exitTask).ConfigureAwait(false);
			}
		}

		process.Dispose();
		return new ExecResult(exitCode, stdout.ToString(), stderr.ToString());
	}

	static void TryKill(MsProcess process)
	{
		try
		{
			process.Signal(MsSignal.SIGKILL);
		}
		catch { }
	}

	// WSLC does not reliably raise Process.Exited after Signal(SIGKILL); poll the process state instead.
	static async Task<int> PollForExitAsync(MsProcess process, Task<int> exitTask)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			if (exitTask.IsCompleted)
			{
				return await exitTask.ConfigureAwait(false);
			}

			var state = process.State;
			if (state is MsProcessState.Exited or MsProcessState.Signalled)
			{
				return process.ExitCode;
			}

			await Task.Delay(100).ConfigureAwait(false);
		}

		return -1;
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
	public virtual Task<string> GetLogsAsync(LogOutput? stream = null, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		return Task.FromResult(_logBuffer.GetLogs(stream));
	}

	/// <inheritdoc />
	public virtual async IAsyncEnumerable<ContainerLogEntry> GetLogsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		foreach (var entry in _logBuffer.Snapshot())
		{
			yield return entry;
		}

		var reader = _logBuffer.Reader;
		await foreach (var entry in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
		{
			yield return entry;
		}
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
				try
				{
					if (_handle.State == MsContainerState.Running)
					{
						await _session
							.StopContainerAsync(
								_handle,
								MsSignal.SIGTERM,
								TimeSpan.FromSeconds(10),
								CancellationToken.None
							)
							.ConfigureAwait(false);
					}
				}
				catch { }

				try
				{
					await _session
						.DeleteContainerAsync(_handle, DeleteContainerOption.Force, CancellationToken.None)
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
			_logBuffer.Complete();
			_handle = null;
			GC.SuppressFinalize(this);
		}
	}

	void EnsureStarted()
	{
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
