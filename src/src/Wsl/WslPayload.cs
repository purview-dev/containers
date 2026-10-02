using System.Reflection;
using System.Runtime.Loader;

namespace Purview.Containers.Wsl;

/// <summary>
/// Loads the Windows build of <c>Purview.Containers.Wsl</c> at runtime so a platform-neutral
/// (<c>net10.0</c>) process on a Windows host can still run containers on WSL Containers.
/// </summary>
/// <remarks>
/// <para>
/// This is the portable half of the backend. It never references <c>Microsoft.WSL.Containers</c> at
/// compile time; instead it loads the Windows build of this same assembly (shipped as a payload,
/// under <c>wslc/</c>) into a dedicated <see cref="AssemblyLoadContext" /> and hands back its
/// <see cref="IContainerBackend" />. Only the platform-neutral <see cref="IContainerBackend" />
/// contract crosses that boundary, so no WSLC type is reflected over.
/// </para>
/// <para>
/// Everywhere else - a non-Windows host, or a missing payload - the probe simply fails, so
/// <c>ContainerBackends</c> auto-selection falls through to Docker. That is what lets the same test
/// project run on WSLC on a developer's Windows machine and on Docker in a Linux CI job without a
/// single configuration change.
/// </para>
/// </remarks>
static class WslPayload
{
	/// <summary>Optional override for the payload folder (advanced hosting scenarios).</summary>
	const string DirectoryEnvironmentVariable = "PURVIEW_CONTAINERS_WSL_PAYLOAD";

	// The default payload folder, copied next to the app by the package's buildTransitive targets.
	const string DirectoryName = "wslc";
	const string ImplementationFileName = "Purview.Containers.Wsl.dll";
	const string BackendTypeName = "Purview.Containers.Wsl.WslContainerBackend";

	static readonly Lock Sync = new();
	static readonly Dictionary<WslContainerRuntimeOptions, IContainerBackend?> ConfiguredBackends = [];
	static readonly Type[] OptionsFactoryParameterTypes =
	[
		typeof(uint?),
		typeof(uint?),
		typeof(bool),
		typeof(string),
		typeof(string),
		typeof(int),
		typeof(long?),
		typeof(bool),
	];
	static bool DefaultAttempted;
	static IContainerBackend? DefaultBackend;
	static string? Failure;

	/// <summary>True when this host could possibly run WSL Containers.</summary>
	internal static bool IsHostSupported => OperatingSystem.IsWindows();

	/// <summary>
	/// Returns the Windows implementation's backend, or <c>null</c> when this host cannot run WSLC
	/// (non-Windows), the payload is absent, or it failed to load. The reason is available from
	/// <see cref="FailureReason" />.
	/// </summary>
	internal static IContainerBackend? TryCreateBackend(WslContainerRuntimeOptions? options = null)
	{
		if (!IsHostSupported)
		{
			Failure = "WSL Containers requires a Windows host.";
			return null;
		}

		lock (Sync)
		{
			if (options is null)
			{
				if (!DefaultAttempted)
				{
					DefaultAttempted = true;
					DefaultBackend = Create(options: null);
				}

				return DefaultBackend;
			}

			if (!ConfiguredBackends.TryGetValue(options, out var backend))
			{
				backend = Create(options);
				ConfiguredBackends[options] = backend;
			}

			return backend;
		}
	}

	/// <summary>The reason the last <see cref="TryCreateBackend" /> returned <c>null</c>.</summary>
	internal static string FailureReason => Failure ?? "The WSL Containers implementation is unavailable.";

	static IContainerBackend? Create(WslContainerRuntimeOptions? options)
	{
		foreach (var directory in CandidateDirectories())
		{
			if (!Directory.Exists(directory))
			{
				continue;
			}

			var candidate = Path.Combine(directory, ImplementationFileName);
			if (!File.Exists(candidate))
			{
				continue;
			}

			try
			{
				PayloadLoadContext context = new(directory);
				var assembly = context.LoadFromAssemblyPath(candidate);
				var backendType = assembly.GetType(BackendTypeName, throwOnError: false);
				var factory = options is null
					? backendType?.GetMethod(
						"Create",
						BindingFlags.Public | BindingFlags.Static,
						binder: null,
						Type.EmptyTypes,
						modifiers: null
					)
					: backendType?.GetMethod(
						"Create",
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
						binder: null,
						OptionsFactoryParameterTypes,
						modifiers: null
					);

				object?[]? arguments = options is null
					? null
					:
					[
						options.CPUCount,
						options.MemorySizeInMB,
						options.EnableGPU,
						options.SessionName,
						options.StoragePath,
						(int)options.StorageMode,
						options.SessionTimeout?.Ticks,
						options.DisableProcessExitCleanup,
					];

				if (factory?.Invoke(null, arguments) is IContainerBackend backend)
				{
					return backend;
				}

				Failure = $"The WSL Containers implementation at '{candidate}' did not expose a backend.";
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				Failure =
					$"The WSL Containers implementation at '{candidate}' failed to load: "
					+ $"{exception.GetType().Name}: {exception.Message}";
			}
		}

		Failure ??=
			"The WSL Containers implementation payload was not found. Reference 'Purview.Containers.Wsl' "
			+ $"with the Windows payload deployed (looked in '{string.Join("', '", CandidateDirectories())}').";
		return null;
	}

	static IEnumerable<string> CandidateDirectories()
	{
		var configured = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
		if (!string.IsNullOrEmpty(configured))
		{
			yield return configured;
		}

		yield return Path.Combine(AppContext.BaseDirectory, DirectoryName);
	}
}

/// <summary>
/// Isolates the WSLC implementation and its Windows SDK dependencies from the host app. Anything not
/// present in the payload folder (notably <c>Purview.Containers</c>) resolves from the default load
/// context, so the shared <see cref="IContainerBackend" /> contract keeps a single identity.
/// </summary>
sealed class PayloadLoadContext(string directory)
	: AssemblyLoadContext("Purview.Containers.Wsl.Payload", isCollectible: false)
{
	readonly string _directory = directory;

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		var candidate = Path.Combine(_directory, $"{assemblyName.Name}.dll");
		return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		var candidate = Path.Combine(_directory, $"{unmanagedDllName}.dll");
		return File.Exists(candidate) ? LoadUnmanagedDllFromPath(candidate) : IntPtr.Zero;
	}
}
