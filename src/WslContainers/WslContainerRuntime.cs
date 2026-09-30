using System.Diagnostics.CodeAnalysis;
using Microsoft.WSL.Containers;

namespace Purview.WslContainers;

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
	private const int ErrorSharingViolation = unchecked((int)0x80070020);

	private static readonly Lazy<WslContainerRuntime> InstanceHolder = new(() => new WslContainerRuntime());

	private readonly WslContainerRuntimeOptions options;
	private readonly object sync = new();
	private WslContainerSession? session;
	private int disposed;

	/// <summary>Creates a runtime with default options. Registers a process-exit cleanup hook.</summary>
	public WslContainerRuntime()
		: this(WslContainerRuntimeOptions.Default) { }

	/// <summary>Creates a runtime with the given options.</summary>
	public WslContainerRuntime(WslContainerRuntimeOptions options)
	{
		this.options = options ?? WslContainerRuntimeOptions.Default;
		if (!this.options.DisableProcessExitCleanup)
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
		ServiceVersion version = WslcService.GetVersion();
		IReadOnlyList<Component> missing = WslcService.GetMissingComponents();
		bool compatible = !missing.Contains(Component.SdkNeedsUpdate);
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
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
		WslContainerSession local = GetOrCreateSession();
		try
		{
			await local.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsSharingViolation(ex) && IsUsingDefaultSharedStore())
		{
			// A concurrent process holds the shared image-store VHD. Isolate this process instead of failing.
			local.DisposeSession();
			local = CreateIsolatedSession();
			session = local;
			await local.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		}

		return local;
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 1)
		{
			return;
		}

		WslContainerSession? local = session;
		if (local is not null)
		{
			await local.TerminateAsync().ConfigureAwait(false);
		}

		GC.SuppressFinalize(this);
	}

	private WslContainerSession GetOrCreateSession()
	{
		WslContainerSession? local = session;
		if (local is not null)
		{
			return local;
		}

		lock (sync)
		{
			local = session;
			if (local is null)
			{
				local = CreateSession();
				session = local;
			}

			return local;
		}
	}

	private WslContainerSession CreateSession()
	{
		string name = NextSessionName();
		string storagePath = ResolveStoragePath(name);
		return new WslContainerSession(name, storagePath, options);
	}

	private WslContainerSession CreateIsolatedSession()
	{
		string name = NextSessionName();
		string storagePath = Path.Combine(PerSessionRoot, name);
		return new WslContainerSession(name, storagePath, options);
	}

	private string ResolveStoragePath(string sessionName)
	{
		if (options.StoragePath is not null)
		{
			return options.StoragePath;
		}

		string? envPath = Environment.GetEnvironmentVariable("WSL_CONTAINERS_STORAGE_PATH");
		if (!string.IsNullOrEmpty(envPath))
		{
			return envPath;
		}

		if (options.StorageMode == StorageMode.PerSession)
		{
			return Path.Combine(PerSessionRoot, sessionName);
		}

		return SharedImagesRoot;
	}

	private bool IsUsingDefaultSharedStore()
	{
		return options.StoragePath is null
			&& string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_CONTAINERS_STORAGE_PATH"))
			&& options.StorageMode == StorageMode.Shared;
	}

	private static string NextSessionName()
	{
		return $"wslc-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
	}

	private static string PerSessionRoot => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Purview",
		"WslContainers",
		"sessions"
	);

	private static string SharedImagesRoot => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Purview",
		"WslContainers",
		"images"
	);

	private static bool IsSharingViolation(Exception exception)
	{
		return exception.HResult == ErrorSharingViolation;
	}

	private void OnProcessExit(object? sender, EventArgs e)
	{
		WslContainerSession? local = session;
		local?.TerminateBestEffort();
	}
}