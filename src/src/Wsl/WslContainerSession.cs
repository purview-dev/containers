using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
	readonly ConcurrentDictionary<string, Task> _activePulls = new(StringComparer.Ordinal);
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
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			container.Stop(signal, timeout);
		}
		finally
		{
			_lifecycleGate.Release();
		}
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

	Task PullDeduplicatedAsync(
		Image reference,
		IProgress<ImagePullProgress>? progress,
		RegistryCredentials? credentials,
		CancellationToken cancellationToken
	)
	{
		return _activePulls.GetOrAdd(
			reference.FullReference,
			static (_, state) =>
				state.Session.PullCoreAsync(
					state.Reference,
					state.Progress,
					state.Credentials,
					state.CancellationToken
				),
			(
				Session: this,
				Reference: reference,
				Progress: progress,
				Credentials: credentials,
				CancellationToken: cancellationToken
			)
		);
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
