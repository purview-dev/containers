using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.WSL.Containers;
using Purview.WslContainers.Images;
using MsContainerState = Microsoft.WSL.Containers.ContainerState;
using MsProcess = Microsoft.WSL.Containers.Process;
using MsProcessState = Microsoft.WSL.Containers.ProcessState;
using MsSignal = Microsoft.WSL.Containers.Signal;

namespace Purview.WslContainers;

/// <summary>A WSLC-backed throwaway container.</summary>
[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Stop/delete/exec teardown is best-effort; WSLC surfaces a wide range of COMException variants."
)]
public class WslContainer : IContainer
{
	private readonly IContainerRuntime runtime;
	private readonly LogBuffer logBuffer = new();
	private WslContainerSession? session;
	private Microsoft.WSL.Containers.Container? handle;
	private IReadOnlyDictionary<ushort, ushort> portMappings = new Dictionary<ushort, ushort>();
	private string? networkIp;
	private int started;
	private int disposed;

	/// <summary>Creates a container bound to a runtime. Prefer building via a <see cref="ContainerBuilder" />.</summary>
	public WslContainer(ContainerConfiguration configuration, IContainerRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(runtime);
		Configuration = configuration;
		this.runtime = runtime;
		Name = configuration.Name ?? GenerateName(configuration);
	}

	/// <summary>The immutable configuration this container was built from.</summary>
	protected ContainerConfiguration Configuration { get; }

	/// <summary>The runtime this container is bound to.</summary>
	protected IContainerRuntime Runtime => runtime;

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public string Id => handle?.Id ?? string.Empty;

	/// <inheritdoc />
	public ContainerState State { get; private set; }

	/// <inheritdoc />
	public string Image => Configuration.Image;

	/// <inheritdoc />
	public virtual async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
		if (Volatile.Read(ref started) == 1)
		{
			return;
		}

		WslContainerSession sessionLocal = (WslContainerSession)
			await runtime.GetSessionAsync(cancellationToken).ConfigureAwait(false);
		session = sessionLocal;
		await session
			.EnsureImageAsync(
				Configuration.Image,
				Configuration.PullPolicy,
				progress: null,
				Configuration.RegistryCredentials,
				cancellationToken
			)
			.ConfigureAwait(false);

		ContainerSettings settings = WslSettingsMapper.ToContainerSettings(Configuration, Name);
		Microsoft.WSL.Containers.Container container = await session
			.CreateContainerAsync(settings, cancellationToken)
			.ConfigureAwait(false);
		handle = container;
		logBuffer.Attach(container.InitProcess);
		await session.StartContainerAsync(container, cancellationToken).ConfigureAwait(false);

		State = MapState(container.State);
		portMappings = WslInspectParser.ParseHostPorts(container.Inspect());
		networkIp = WslInspectParser.TryGetNetworkIp(container.Inspect());
		Volatile.Write(ref started, 1);

		if (Configuration.WaitStrategies.Count > 0)
		{
			WaitContext waitContext = new(this, portMappings, networkIp);
			await WaitStrategyRunner
				.RunAsync(waitContext, Configuration.WaitStrategies, Configuration.StartupTimeout, cancellationToken)
				.ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public virtual async Task StopAsync(CancellationToken cancellationToken = default)
	{
		EnsureStarted();
		if (handle is null || session is null)
		{
			return;
		}

		try
		{
			await session
				.StopContainerAsync(handle, MsSignal.SIGTERM, TimeSpan.FromSeconds(10), cancellationToken)
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

		State = MapState(handle.State);
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
		if (handle is null || session is null)
		{
			throw new InvalidOperationException("Container is not started.");
		}

		ProcessSettings settings = new ProcessSettings
		{
			CommandLine = new List<string>(command),
			OutputMode = ProcessOutputMode.Event,
			WorkingDirectory = options?.WorkingDirectory,
		};
		if (options?.Environment is { Count: > 0 })
		{
			settings.EnvironmentVariables = new Dictionary<string, string>(options.Environment);
		}

		MsProcess process = await session.CreateProcessAsync(handle, settings, cancellationToken).ConfigureAwait(false);
		using CancellationTokenRegistration cancelRegistration = cancellationToken.Register(
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

		StringBuilder stdout = new StringBuilder();
		StringBuilder stderr = new StringBuilder();
		TaskCompletionSource<int> exitSource = new TaskCompletionSource<int>(
			TaskCreationOptions.RunContinuationsAsynchronously
		);
		process.OutputReceived += data => stdout.Append(Encoding.UTF8.GetString(data));
		process.ErrorReceived += data => stderr.Append(Encoding.UTF8.GetString(data));
		process.Exited += code => exitSource.TrySetResult(code);

		await session.StartProcessAsync(process, cancellationToken).ConfigureAwait(false);

		Task<int> exitTask = exitSource.Task;
		int exitCode;
		if (options?.Timeout is TimeSpan timeout)
		{
			Task completed = await Task.WhenAny(exitTask, Task.Delay(timeout, CancellationToken.None))
				.ConfigureAwait(false);
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

	private static void TryKill(MsProcess process)
	{
		try
		{
			process.Signal(MsSignal.SIGKILL);
		}
		catch { }
	}

	// WSLC does not reliably raise Process.Exited after Signal(SIGKILL); poll the process state instead.
	private static async Task<int> PollForExitAsync(MsProcess process, Task<int> exitTask)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			if (exitTask.IsCompleted)
			{
				return await exitTask.ConfigureAwait(false);
			}

			MsProcessState state = process.State;
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
		if (!portMappings.TryGetValue(containerPort, out ushort hostPort))
		{
			throw new WslContainerException($"No host port mapping found for container port {containerPort}.");
		}

		return hostPort;
	}

	/// <inheritdoc />
	public virtual IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts()
	{
		EnsureStarted();
		return portMappings;
	}

	/// <inheritdoc />
	public virtual Task<string> GetLogsAsync(LogOutput? stream = null, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
		return Task.FromResult(logBuffer.GetLogs(stream));
	}

	/// <inheritdoc />
	public virtual async IAsyncEnumerable<ContainerLogEntry> GetLogsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
		foreach (ContainerLogEntry entry in logBuffer.Snapshot())
		{
			yield return entry;
		}

		ChannelReader<ContainerLogEntry> reader = logBuffer.Reader;
		await foreach (ContainerLogEntry entry in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
		{
			yield return entry;
		}
	}

	/// <inheritdoc />
	public virtual async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 1)
		{
			return;
		}

		try
		{
			if (handle is not null && session is not null)
			{
				try
				{
					if (handle.State == MsContainerState.Running)
					{
						await session
							.StopContainerAsync(
								handle,
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
					await session
						.DeleteContainerAsync(handle, DeleteContainerOption.Force, CancellationToken.None)
						.ConfigureAwait(false);
				}
				catch { }

				try
				{
					handle.Dispose();
				}
				catch { }
			}
		}
		finally
		{
			handle = null;
			GC.SuppressFinalize(this);
		}
	}

	private void EnsureStarted()
	{
		if (Volatile.Read(ref started) == 0)
		{
			throw new InvalidOperationException("Container has not been started. Call StartAsync() first.");
		}
	}

	private static ContainerState MapState(MsContainerState state)
	{
		return (ContainerState)state;
	}

	private static string GenerateName(ContainerConfiguration configuration)
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

		string safe = new string(
			shortName
				.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-')
				.ToArray()
		);
		return $"{safe}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..6]}";
	}
}
