using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.WSL.Containers;
using Purview.Containers.Images;
using Purview.Containers.Wsl.Diagnostics;
using Windows.Foundation;
using MsContainer = Microsoft.WSL.Containers.Container;
using Process = Microsoft.WSL.Containers.Process;

namespace Purview.Containers.Wsl;

[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Session termination and pull cleanup are best-effort; WSLC surfaces a wide range of COMException variants."
)]
sealed class WslContainerSession : IContainerSession
{
	readonly Session _session;
	readonly SemaphoreSlim _lifecycleGate = new(1, 1);
	readonly ConcurrentDictionary<string, Lazy<Task>> _activePulls = new(StringComparer.Ordinal);
	readonly string _storagePath;
	readonly bool _deleteStorageOnTerminate;
	int _started;
	int _terminated;

	public WslContainerSession(
		string name,
		string storagePath,
		WslContainerRuntimeOptions options,
		bool deleteStorageOnTerminate = false
	)
	{
		Name = name;
		_storagePath = storagePath;
		_deleteStorageOnTerminate = deleteStorageOnTerminate;
		SessionSettings settings = new(name, storagePath)
		{
			CpuCount = options.CPUCount,
			MemorySizeInMB = options.MemorySizeInMB,
			EnableGpu = options.EnableGPU,
			Timeout = options.SessionTimeout,
		};
		_session = new Session(settings);
		_session.Terminated += reason => Terminated?.Invoke(reason);
	}

	/// <summary>Raised when the underlying session terminates.</summary>
	internal event Action<SessionTerminationReason>? Terminated;

	public string Name { get; }

	public async Task EnsureStartedAsync(CancellationToken cancellationToken)
	{
		using var activity = WslContainerActivity.Source.StartActivity("wslcontainer.session.start");
		activity?.SetTag("session.name", Name);
		ThrowIfTerminated();
		if (Volatile.Read(ref _started) == 1)
		{
			return;
		}

		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			if (Volatile.Read(ref _started) == 0)
			{
				_session.Start();
				Volatile.Write(ref _started, 1);
			}
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async Task EnsureImageAsync(
		string image,
		PullPolicy policy,
		IProgress<ImagePullProgress>? progress,
		RegistryCredentials? credentials,
		CancellationToken cancellationToken
	)
	{
		await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		var reference = Image.Parse(image);
		switch (policy)
		{
			case PullPolicy.Never:
				if (!await ContainsImageAsync(reference, cancellationToken).ConfigureAwait(false))
				{
					throw new WslContainerException($"Image '{image}' is not present and PullPolicy.Never is set.");
				}

				return;
			case PullPolicy.Missing:
				if (await ContainsImageAsync(reference, cancellationToken).ConfigureAwait(false))
				{
					return;
				}

				await PullDeduplicatedAsync(reference, progress, credentials, cancellationToken).ConfigureAwait(false);
				return;
			case PullPolicy.Always:
				await PullDeduplicatedAsync(reference, progress, credentials, cancellationToken).ConfigureAwait(false);
				return;
			default:
				throw new ArgumentOutOfRangeException(nameof(policy), policy, null);
		}
	}

	public async Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken)
	{
		await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		return await ReadImagesAsync(cancellationToken).ConfigureAwait(false);
	}

	// GetImages() reads (and locks) the session's storage VHD, so it is gated with the other
	// session-mutating operations rather than treated as a side-effect-free read.
	async Task<IReadOnlyList<ImageSummary>> ReadImagesAsync(CancellationToken cancellationToken)
	{
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			return _session
				.GetImages()
				.Select(image => new ImageSummary(image.Name, image.Size, image.CreatedTimestamp))
				.ToArray();
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async Task TerminateAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (Interlocked.Exchange(ref _terminated, 1) == 1)
		{
			return;
		}

		try
		{
			_session.Terminate();
		}
		catch { }

		try
		{
			_session.Dispose();
		}
		catch { }

		_lifecycleGate.Dispose();
		TryDeleteStorage();
		await Task.CompletedTask;
	}

	/// <summary>Synchronous best-effort terminate for the process-exit hook.</summary>
	internal void TerminateBestEffort()
	{
		if (Interlocked.Exchange(ref _terminated, 1) == 1)
		{
			return;
		}

		try
		{
			_session.Terminate();
		}
		catch { }

		try
		{
			_session.Dispose();
		}
		catch { }

		_lifecycleGate.Dispose();
		TryDeleteStorage();
	}

	/// <summary>Releases a session that failed to start (e.g. a shared-store contention fallback).</summary>
	internal void DisposeSession()
	{
		if (Interlocked.Exchange(ref _terminated, 1) == 1)
		{
			return;
		}

		try
		{
			_session.Dispose();
		}
		catch { }

		_lifecycleGate.Dispose();
	}

	internal async Task<MsContainer> CreateContainerAsync(
		ContainerSettings settings,
		CancellationToken cancellationToken
	)
	{
		using var activity = WslContainerActivity.Source.StartActivity("wslcontainer.container.create");
		activity?.SetTag("container.name", settings.Name);
		await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			return _session.CreateContainer(settings);
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	internal async Task<MsContainer> CreateConfiguredContainerAsync(
		ContainerConfiguration configuration,
		string name,
		CancellationToken cancellationToken
	)
	{
		await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		string? environmentFile = null;
		var owner = Guid.NewGuid().ToString("N");
		try
		{
			ThrowIfTerminated();
			var hash = WslCreateArguments.ReuseHash(configuration);
			if (configuration.Reuse)
			{
				var inspect = await WslCommand
					.RunAsync(Name, ["inspect", "--format", "json", name], cancellationToken, allowFailure: true)
					.ConfigureAwait(false);
				if (inspect.ExitCode == 0)
				{
					using var document = JsonDocument.Parse(inspect.Output);
					var labels = document.RootElement[0].GetProperty("Config").GetProperty("Labels");
					if (!labels.TryGetProperty("purview.containers.reuse", out var value) || value.GetString() != hash)
						throw new WslContainerException(
							"A reusable container with this name exists but has different configuration."
						);
					if (document.RootElement[0].GetProperty("State").GetProperty("Status").GetString() == "exited")
						await WslCommand.RunAsync(Name, ["start", name], cancellationToken).ConfigureAwait(false);
					return _session.OpenContainer(name, ProcessOutputMode.Event);
				}
			}

			if (configuration.Environment.Count > 0)
			{
				WslCreateArguments.ValidateEnvironment(configuration);
				environmentFile = Path.GetTempFileName();
				await File.WriteAllLinesAsync(
						environmentFile,
						configuration.Environment.Select(pair => $"{pair.Key}={pair.Value}"),
						cancellationToken
					)
					.ConfigureAwait(false);
			}
			var args = WslCreateArguments.Build(configuration, name, hash, owner, environmentFile);
			await WslCommand.RunAsync(Name, args, cancellationToken).ConfigureAwait(false);
			return _session.OpenContainer(name, ProcessOutputMode.Event);
		}
		catch
		{
			// Cancellation can arrive after the CLI created the object but before it returned its ID.
			if (!configuration.Reuse)
			{
				using CancellationTokenSource cleanup = new(configuration.CleanupTimeout);
				try
				{
					var inspect = await WslCommand
						.RunAsync(Name, ["inspect", "--format", "json", name], cleanup.Token, allowFailure: true)
						.ConfigureAwait(false);
					if (inspect.ExitCode == 0)
					{
						using var document = JsonDocument.Parse(inspect.Output);
						var labels = document.RootElement[0].GetProperty("Config").GetProperty("Labels");
						if (
							labels.TryGetProperty("purview.containers.owner", out var value)
							&& value.GetString() == owner
						)
							await WslCommand
								.RunAsync(Name, ["remove", "--force", name], cleanup.Token, allowFailure: true)
								.ConfigureAwait(false);
					}
				}
				catch { }
			}
			throw;
		}
		finally
		{
			try
			{
				if (environmentFile is not null)
					File.Delete(environmentFile);
			}
			finally
			{
				_lifecycleGate.Release();
			}
		}
	}

	internal async Task RunCommandAsync(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(timeout);
		await _lifecycleGate.WaitAsync(deadline.Token).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			await WslCommand.RunAsync(Name, arguments, deadline.Token, captureOutput: false).ConfigureAwait(false);
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	internal async Task<WslCommand.Result> RunCopyCommandAsync(
		string[] arguments,
		CancellationToken cancellationToken,
		Stream? input = null
	)
	{
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			return await WslCommand.RunAsync(Name, arguments, cancellationToken, input).ConfigureAwait(false);
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	internal async Task StartContainerAsync(MsContainer container, CancellationToken cancellationToken)
	{
		using var activity = WslContainerActivity.Source.StartActivity("wslcontainer.container.start");
		activity?.SetTag("container.id", container.Id);
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			container.Start();
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	internal async Task StopContainerAsync(
		MsContainer container,
		Signal signal,
		TimeSpan timeout,
		CancellationToken cancellationToken
	)
	{
		using var activity = WslContainerActivity.Source.StartActivity("wslcontainer.container.stop");
		activity?.SetTag("container.id", container.Id);
		// Native Container.Stop can block beyond its timeout while holding the session gate.
		// Signal PID 1 directly and poll with managed delays so other containers can still make progress.
		await SignalContainerAsync(container, signal, cancellationToken).ConfigureAwait(false);
		if (await WaitForContainerExitAsync(container, timeout, cancellationToken).ConfigureAwait(false))
		{
			return;
		}

		await SignalContainerAsync(container, Signal.SIGKILL, cancellationToken).ConfigureAwait(false);
		if (
			!await WaitForContainerExitAsync(container, TimeSpan.FromSeconds(5), cancellationToken)
				.ConfigureAwait(false)
		)
		{
			throw new TimeoutException("The WSL container did not exit after SIGKILL.");
		}
	}

	async Task SignalContainerAsync(MsContainer container, Signal signal, CancellationToken cancellationToken)
	{
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			if (container.State == Microsoft.WSL.Containers.ContainerState.Running)
			{
				container.InitProcess.Signal(signal);
			}
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	async Task<bool> WaitForContainerExitAsync(
		MsContainer container,
		TimeSpan timeout,
		CancellationToken cancellationToken
	)
	{
		var stopwatch = Stopwatch.StartNew();
		while (container.State == Microsoft.WSL.Containers.ContainerState.Running)
		{
			ThrowIfTerminated();
			cancellationToken.ThrowIfCancellationRequested();
			var remaining = timeout - stopwatch.Elapsed;
			if (remaining <= TimeSpan.Zero)
			{
				return false;
			}

			await Task.Delay(
					remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100),
					cancellationToken
				)
				.ConfigureAwait(false);
		}

		return true;
	}

	internal async Task DeleteContainerAsync(
		MsContainer container,
		DeleteContainerOption option,
		CancellationToken cancellationToken
	)
	{
		using var activity = WslContainerActivity.Source.StartActivity("wslcontainer.container.delete");
		activity?.SetTag("container.id", container.Id);
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			container.Delete(option);
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	internal async Task<Process> CreateProcessAsync(
		MsContainer container,
		ProcessSettings settings,
		CancellationToken cancellationToken
	)
	{
		using var activity = WslContainerActivity.Source.StartActivity("wslcontainer.exec.create");
		activity?.SetTag("container.id", container.Id);
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			return container.CreateProcess(settings);
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	internal async Task StartProcessAsync(Process process, CancellationToken cancellationToken)
	{
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			process.Start();
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await TerminateAsync().ConfigureAwait(false);
	}

	async Task<bool> ContainsImageAsync(Image reference, CancellationToken cancellationToken)
	{
		await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		var images = await ReadImagesAsync(cancellationToken).ConfigureAwait(false);
		return images.Any(image => reference.MatchesStoredName(image.Name));
	}

	async Task PullDeduplicatedAsync(
		Image reference,
		IProgress<ImagePullProgress>? progress,
		RegistryCredentials? credentials,
		CancellationToken cancellationToken
	)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// The pull is shared by every caller waiting on the same image, so it must not inherit one
		// caller's cancellation token: a test timing out would otherwise cancel the pull for every
		// other test waiting on it. Callers stop waiting on their own token; the pull itself runs to
		// completion (or fails) once.
		var lazy = _activePulls.GetOrAdd(
			reference.FullReference,
			_ => new Lazy<Task>(
				() => PullWithDeadlineAsync(reference, progress, credentials),
				LazyThreadSafetyMode.ExecutionAndPublication
			)
		);
		var pull = lazy.Value;

		// Observe a fault even if every waiter cancelled, so it is not raised as an unobserved
		// task exception at finalization.
		_ = pull.ContinueWith(
			static task => _ = task.Exception,
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default
		);

		await pull.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	async Task PullWithDeadlineAsync(
		Image reference,
		IProgress<ImagePullProgress>? progress,
		RegistryCredentials? credentials
	)
	{
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(5));
		await PullCoreAsync(reference, progress, credentials, deadline.Token)
			.WaitAsync(deadline.Token)
			.ConfigureAwait(false);
	}

	async Task PullCoreAsync(
		Image reference,
		IProgress<ImagePullProgress>? progress,
		RegistryCredentials? credentials,
		CancellationToken cancellationToken
	)
	{
		using var activity = WslContainerActivity.Source.StartActivity("wslcontainer.image.pull");
		activity?.SetTag("image", reference.FullReference);
		PullImageOptions options = new(reference.FullReference);
		if (credentials is not null)
		{
			var authentication = _session.Authenticate(
				credentials.ServerAddress,
				credentials.Username,
				credentials.Password.Value
			);
			options.RegistryAuth = authentication.IdentityToken;
		}

		var operation = _session.PullImageAsync(options);
		using var registration = cancellationToken.Register(
			static state =>
			{
				if (state is IAsyncActionWithProgress<ImageProgress> op)
				{
					try
					{
						op.Cancel();
					}
					catch { }
				}
			},
			operation
		);
		if (progress is not null)
		{
			operation.Progress = (_, p) =>
				progress.Report(new ImagePullProgress(p.Id, p.Status.ToString(), p.CurrentBytes, p.TotalBytes));
		}

		try
		{
			await operation;
		}
		finally
		{
			_activePulls.TryRemove(reference.FullReference, out _);
		}
	}

	void TryDeleteStorage()
	{
		if (!_deleteStorageOnTerminate)
		{
			return;
		}

		try
		{
			if (Directory.Exists(_storagePath))
			{
				Directory.Delete(_storagePath, recursive: true);
			}
		}
		catch (IOException)
		{
			// The session manager may still hold the store VHD briefly; the store is transient.
		}
		catch (UnauthorizedAccessException)
		{
			// Same.
		}
	}

	void ThrowIfTerminated()
	{
		if (Volatile.Read(ref _terminated) == 1)
		{
			throw new WslContainerSessionTerminatedException($"Session '{Name}' has been terminated.");
		}
	}
}
