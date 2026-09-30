using System.Diagnostics.CodeAnalysis;
using Microsoft.WSL.Containers;

namespace Purview.Containers.Wsl;

/// <summary>
/// The process-wide WSL Containers runtime. Owns a single lazily started WSLC session.
/// Images are shared by default: sessions use a stable shared storage directory so the image store is
/// pulled once and reused across process runs. When a concurrent process holds that store, the runtime
/// falls back to an isolated per-process store automatically.
/// </summary>
[SuppressMessage(
	"Design",
	"CA2213:Disposable fields should be disposed",
	Justification = "The session is released asynchronously in DisposeAsync/TerminateAsync; CA2213 cannot see the async path."
)]
public sealed class WslContainerRuntime : IContainerRuntime
{
	const int ErrorSharingViolation = unchecked((int)0x80070020);

	/// <summary>
	/// Environment variable that overrides the session storage path for every runtime that does not set
	/// <see cref="WslContainerRuntimeOptions.StoragePath" />.
	/// </summary>
	public const string StoragePathEnvironmentVariable = "PURVIEW_CONTAINERS_STORAGE_PATH";

	/// <summary>The pre-rename storage path variable, still honoured as a fallback.</summary>
	public const string LegacyStoragePathEnvironmentVariable = "WSL_CONTAINERS_STORAGE_PATH";

	static readonly Lazy<WslContainerRuntime> InstanceHolder = new(() => new WslContainerRuntime());

	readonly WslContainerRuntimeOptions _options;
	readonly Lock _sync = new();
	readonly SemaphoreSlim _sessionGate = new(1, 1);
	WslContainerSession? _session;
	int _disposed;
	int _storeVerified;

	/// <summary>Creates a runtime with default options. Registers a process-exit cleanup hook.</summary>
	public WslContainerRuntime()
		: this(WslContainerRuntimeOptions.Default) { }

	/// <summary>Creates a runtime with the given options.</summary>
	public WslContainerRuntime(WslContainerRuntimeOptions options)
	{
		_options = options ?? WslContainerRuntimeOptions.Default;
		if (!_options.DisableProcessExitCleanup)
		{
			AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		}
	}

	/// <summary>The default process-wide runtime.</summary>
	public static WslContainerRuntime Instance => InstanceHolder.Value;

	/// <inheritdoc />
	public async Task<WslContainerRuntimeInfo> GetInfoAsync(CancellationToken cancellationToken = default)
	{
		await Task.Yield();
		var version = WslcService.GetVersion();
		var missing = WslcService.GetMissingComponents();
		var compatible = !missing.Contains(Component.SdkNeedsUpdate);
		return new WslContainerRuntimeInfo(
			$"{version.Major}.{version.Minor}.{version.Revision}",
			missing.Select(component => component.ToString()).ToArray(),
			missing.Count == 0,
			compatible
		);
	}

	/// <inheritdoc />
	public async Task<IContainerSession> GetSessionAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		var local = GetOrCreateSession();
		if (Volatile.Read(ref _storeVerified) == 1)
		{
			await local.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
			return local;
		}

		// A session's image store lives in storage.vhdx and is opened lazily on first access rather than
		// at session start, so contention from a concurrent process can surface on the first store read
		// instead of during start. Verify the store once, under a gate, and fall back to an isolated
		// per-process store when another process holds it.
		await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
			local = GetOrCreateSession();
			if (Volatile.Read(ref _storeVerified) == 0)
			{
				local = await EnsureStoreAsync(local, cancellationToken).ConfigureAwait(false);
				Volatile.Write(ref _storeVerified, 1);
			}
			else
			{
				await local.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
			}

			return local;
		}
		finally
		{
			_sessionGate.Release();
		}
	}

	async Task<WslContainerSession> EnsureStoreAsync(WslContainerSession session, CancellationToken cancellationToken)
	{
		try
		{
			await session.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
			// Opens (and therefore locks) storage.vhdx; throws 0x80070020 when another process holds it.
			await session.ListImagesAsync(cancellationToken).ConfigureAwait(false);
			return session;
		}
		catch (Exception ex) when (ShouldFallBackToIsolatedStore(ex, IsUsingDefaultSharedStore()))
		{
			// A concurrent process holds the shared image-store VHD. Isolate this process instead of failing.
			session.DisposeSession();
			var isolated = CreateIsolatedSession();
			_session = isolated;
			await isolated.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
			await isolated.ListImagesAsync(cancellationToken).ConfigureAwait(false);
			return isolated;
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		var local = _session;
		if (local is not null)
		{
			await local.TerminateAsync().ConfigureAwait(false);
		}

		_sessionGate.Dispose();
		GC.SuppressFinalize(this);
	}

	WslContainerSession GetOrCreateSession()
	{
		var local = _session;
		if (local is not null)
		{
			return local;
		}

		lock (_sync)
		{
			local = _session;
			if (local is null)
			{
				local = CreateSession();
				_session = local;
			}

			return local;
		}
	}

	WslContainerSession CreateSession()
	{
		var name = NextSessionName();
		var storagePath = ResolveStoragePath(name);
		return new WslContainerSession(name, storagePath, _options);
	}

	WslContainerSession CreateIsolatedSession()
	{
		var name = NextSessionName();
		var storagePath = Path.Combine(PerSessionRoot, name);
		// An isolated fallback store is a throwaway: it is removed when the session terminates so
		// contended runs do not accumulate per-process image stores.
		return new WslContainerSession(name, storagePath, _options, deleteStorageOnTerminate: true);
	}

	string ResolveStoragePath(string sessionName)
	{
		if (_options.StoragePath is not null)
		{
			return _options.StoragePath;
		}

		var envPath = StoragePathFromEnvironment();
		if (!string.IsNullOrEmpty(envPath))
		{
			return envPath;
		}

		if (_options.StorageMode == StorageMode.PerSession)
		{
			return Path.Combine(PerSessionRoot, sessionName);
		}

		// Default to a shared store in the user's local application data directory.
		return SharedImagesRoot;
	}

	bool IsUsingDefaultSharedStore()
	{
		return _options.StoragePath is null
			&& string.IsNullOrEmpty(StoragePathFromEnvironment())
			&& _options.StorageMode == StorageMode.Shared;
	}

	/// <summary>
	/// The storage path configured through the environment: <see cref="StoragePathEnvironmentVariable" />
	/// first, then the pre-rename <see cref="LegacyStoragePathEnvironmentVariable" />.
	/// </summary>
	static string? StoragePathFromEnvironment()
	{
		var configured = Environment.GetEnvironmentVariable(StoragePathEnvironmentVariable);
		return string.IsNullOrEmpty(configured)
			? Environment.GetEnvironmentVariable(LegacyStoragePathEnvironmentVariable)
			: configured;
	}

	static string NextSessionName()
	{
		return $"wslc-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
	}

	static string PerSessionRoot =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Purview",
			"WslContainers",
			"sessions"
		);

	static string SharedImagesRoot =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Purview",
			"WslContainers",
			"images"
		);

	static bool IsSharingViolation(Exception exception)
	{
		return exception.HResult == ErrorSharingViolation;
	}

	/// <summary>
	/// True when <paramref name="exception" /> is a Windows sharing violation (<c>0x80070020</c>) and the
	/// runtime is using the default shared image store, meaning a concurrent process holds the store VHD
	/// and this process should fall back to an isolated per-process store.
	/// </summary>
	internal static bool ShouldFallBackToIsolatedStore(Exception exception, bool isUsingDefaultSharedStore)
	{
		return isUsingDefaultSharedStore && IsSharingViolation(exception);
	}

	void OnProcessExit(object? sender, EventArgs e)
	{
		var local = _session;
		local?.TerminateBestEffort();
	}
}
