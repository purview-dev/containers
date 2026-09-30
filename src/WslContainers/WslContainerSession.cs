using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.WSL.Containers;
using Purview.WslContainers.Images;
using Windows.Foundation;
using Process = Microsoft.WSL.Containers.Process;

namespace Purview.WslContainers;

[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Session termination and pull cleanup are best-effort; WSLC surfaces a wide range of COMException variants."
)]
internal sealed class WslContainerSession : IContainerSession
{
	private readonly Session session;
	private readonly string name;
	private readonly SemaphoreSlim lifecycleGate = new(1, 1);
	private readonly ConcurrentDictionary<string, Task> activePulls = new(StringComparer.Ordinal);
	private int started;
	private int terminated;

	public WslContainerSession(string name, string storagePath, WslContainerRuntimeOptions options)
	{
		this.name = name;
		SessionSettings settings = new SessionSettings(name, storagePath)
		{
			CpuCount = options.CpuCount,
			MemorySizeInMB = options.MemorySizeInMB,
			EnableGpu = options.EnableGpu,
			Timeout = options.SessionTimeout,
		};
		session = new Session(settings);
		session.Terminated += reason => Terminated?.Invoke(reason);
	}

	/// <summary>Raised when the underlying session terminates.</summary>
	internal event Action<SessionTerminationReason>? Terminated;

	public string Name => name;

	public async Task EnsureStartedAsync(CancellationToken cancellationToken)
	{
		using Activity? activity = WslContainersActivity.Source.StartActivity("wslcontainer.session.start");
		activity?.SetTag("session.name", name);
		ThrowIfTerminated();
		if (Volatile.Read(ref started) == 1)
		{
			return;
		}

		await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			if (Volatile.Read(ref started) == 0)
			{
				session.Start();
				Volatile.Write(ref started, 1);
			}
		}
		finally
		{
			lifecycleGate.Release();
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
		Image reference = Image.Parse(image);
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
		return session
			.GetImages()
			.Select(image => new ImageSummary(image.Name, image.Size, image.CreatedTimestamp))
			.ToArray();
	}

	public async Task TerminateAsync(CancellationToken cancellationToken = default)
	{
		if (Interlocked.Exchange(ref terminated, 1) == 1)
		{
			return;
		}

		try
		{
			session.Terminate();
		}
		catch { }

		try
		{
			session.Dispose();
		}
		catch { }

		lifecycleGate.Dispose();
		await Task.CompletedTask;
	}

	/// <summary>Synchronous best-effort terminate for the process-exit hook.</summary>
	internal void TerminateBestEffort()
	{
		if (Interlocked.Exchange(ref terminated, 1) == 1)
		{
			return;
		}

		try
		{
			session.Terminate();
		}
		catch { }

		try
		{
			session.Dispose();
		}
		catch { }

		lifecycleGate.Dispose();
	}

	/// <summary>Releases a session that failed to start (e.g. a shared-store contention fallback).</summary>
	internal void DisposeSession()
	{
		if (Interlocked.Exchange(ref terminated, 1) == 1)
		{
			return;
		}

		try
		{
			session.Dispose();
		}
		catch { }

		lifecycleGate.Dispose();
	}

	internal async Task<Container> CreateContainerAsync(ContainerSettings settings, CancellationToken cancellationToken)
	{
		using Activity? activity = WslContainersActivity.Source.StartActivity("wslcontainer.container.create");
		activity?.SetTag("container.name", settings.Name);
		await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			return session.CreateContainer(settings);
		}
		finally
		{
			lifecycleGate.Release();
		}
	}

	internal async Task StartContainerAsync(Container container, CancellationToken cancellationToken)
	{
		using Activity? activity = WslContainersActivity.Source.StartActivity("wslcontainer.container.start");
		activity?.SetTag("container.id", container.Id);
		await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			container.Start();
		}
		finally
		{
			lifecycleGate.Release();
		}
	}

	internal async Task StopContainerAsync(
		Container container,
		Signal signal,
		TimeSpan timeout,
		CancellationToken cancellationToken
	)
	{
		using Activity? activity = WslContainersActivity.Source.StartActivity("wslcontainer.container.stop");
		activity?.SetTag("container.id", container.Id);
		await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			container.Stop(signal, timeout);
		}
		finally
		{
			lifecycleGate.Release();
		}
	}

	internal async Task DeleteContainerAsync(
		Container container,
		DeleteContainerOption option,
		CancellationToken cancellationToken
	)
	{
		using Activity? activity = WslContainersActivity.Source.StartActivity("wslcontainer.container.delete");
		activity?.SetTag("container.id", container.Id);
		await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			container.Delete(option);
		}
		finally
		{
			lifecycleGate.Release();
		}
	}

	internal async Task<Process> CreateProcessAsync(
		Container container,
		ProcessSettings settings,
		CancellationToken cancellationToken
	)
	{
		using Activity? activity = WslContainersActivity.Source.StartActivity("wslcontainer.exec.create");
		activity?.SetTag("container.id", container.Id);
		await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			return container.CreateProcess(settings);
		}
		finally
		{
			lifecycleGate.Release();
		}
	}

	internal async Task StartProcessAsync(Process process, CancellationToken cancellationToken)
	{
		await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfTerminated();
			process.Start();
		}
		finally
		{
			lifecycleGate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await TerminateAsync().ConfigureAwait(false);
	}

	private async Task<bool> ContainsImageAsync(Image reference, CancellationToken cancellationToken)
	{
		await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		return session.GetImages().Any(image => reference.MatchesStoredName(image.Name));
	}

	private Task PullDeduplicatedAsync(
		Image reference,
		IProgress<ImagePullProgress>? progress,
		RegistryCredentials? credentials,
		CancellationToken cancellationToken
	)
	{
		return activePulls.GetOrAdd(
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

	private async Task PullCoreAsync(
		Image reference,
		IProgress<ImagePullProgress>? progress,
		RegistryCredentials? credentials,
		CancellationToken cancellationToken
	)
	{
		using Activity? activity = WslContainersActivity.Source.StartActivity("wslcontainer.image.pull");
		activity?.SetTag("image", reference.FullReference);
		PullImageOptions options = new PullImageOptions(reference.FullReference);
		if (credentials is not null)
		{
			AuthenticateResult authentication = session.Authenticate(
				credentials.ServerAddress,
				credentials.Username,
				credentials.Password.Value
			);
			options.RegistryAuth = authentication.IdentityToken;
		}

		IAsyncActionWithProgress<ImageProgress> operation = session.PullImageAsync(options);
		using CancellationTokenRegistration registration = cancellationToken.Register(
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
			activePulls.TryRemove(reference.FullReference, out _);
		}
	}

	private void ThrowIfTerminated()
	{
		if (Volatile.Read(ref terminated) == 1)
		{
			throw new WslContainerSessionTerminatedException($"Session '{name}' has been terminated.");
		}
	}
}
