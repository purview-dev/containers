using System.Text;
using Purview.Containers.Runtime;

namespace Purview.Containers;

/// <summary>
/// Registry and selection point for the container backends available to this process.
/// </summary>
/// <remarks>
/// <para>
/// Backend packages register themselves from the consuming assembly through the generated module
/// initializer in <c>Purview.Containers.Backends.targets</c>, so a package consumer gets registration
/// without reflection and without depending on which assemblies the runtime happens to load. A
/// project-reference consumer (this repository's own tests, for example) can register explicitly with
/// <see cref="Register" />.
/// </para>
/// <para>
/// The backend that actually runs is chosen by <see cref="ResolveAsync" /> using this precedence:
/// a backend pinned with <see cref="Use(IContainerBackend)" />, then the requested
/// <see cref="Selection" /> (set in code or read from <see cref="SelectionEnvironmentVariable" />), then
/// automatic probing of every registered backend. A named backend never falls back to another one: the
/// failure carries that backend's own diagnostics.
/// </para>
/// </remarks>
public static class ContainerBackends
{
	/// <summary>
	/// Environment variable that selects the backend: <c>auto</c> (the default), or the name of a
	/// registered backend such as <c>wsl</c> or <c>docker</c>. Case-insensitive; an unrecognised value
	/// fails the resolution rather than being ignored.
	/// </summary>
	public const string SelectionEnvironmentVariable = "PURVIEW_CONTAINERS_BACKEND";

	static readonly Lock Sync = new();
	static readonly List<IContainerBackend> Registered = [];
	static IContainerBackend? PinnedBackend;
	static ContainerBackendSelection? PinnedSelection;
	static Task<IContainerBackend>? Resolution;

	/// <summary>Every registered backend, in registration order.</summary>
	public static IReadOnlyList<IContainerBackend> All
	{
		get
		{
			lock (Sync)
			{
				return [.. Registered];
			}
		}
	}

	/// <summary>
	/// The requested selection: the value set with <see cref="Use(ContainerBackendSelection?)" />, otherwise
	/// the <see cref="SelectionEnvironmentVariable" /> value, otherwise
	/// <see cref="ContainerBackendSelection.Auto" />.
	/// </summary>
	public static ContainerBackendSelection Selection
	{
		get
		{
			lock (Sync)
			{
				return PinnedSelection ?? ContainerBackendSelection.FromEnvironment();
			}
		}
	}

	/// <summary>
	/// Registers a backend. Idempotent: registering a backend whose <see cref="IContainerBackend.Name" />
	/// is already present replaces that entry, so an explicitly registered backend and a generated
	/// registration can coexist.
	/// </summary>
	public static void Register(IContainerBackend backend)
	{
		ArgumentNullException.ThrowIfNull(backend);
		lock (Sync)
		{
			var index = Registered.FindIndex(existing =>
				string.Equals(existing.Name, backend.Name, StringComparison.Ordinal)
			);
			if (index >= 0)
			{
				Registered[index] = backend;
			}
			else
			{
				Registered.Add(backend);
			}

			Resolution = null;
		}
	}

	/// <summary>
	/// Pins a backend instance, which then wins over every other selection rule. Pass <c>null</c> to return
	/// to the configured selection.
	/// </summary>
	public static void Use(IContainerBackend? backend)
	{
		lock (Sync)
		{
			PinnedBackend = backend;
			Resolution = null;
		}
	}

	/// <summary>
	/// Pins the selection (automatic detection or one named backend), which wins over the environment
	/// variable. Pass <c>null</c> to fall back to the environment.
	/// </summary>
	public static void Use(ContainerBackendSelection? selection)
	{
		lock (Sync)
		{
			PinnedSelection = selection;
			Resolution = null;
		}
	}

	/// <summary>
	/// Resolves the backend to use, probing usability when necessary and caching the result for the process.
	/// </summary>
	/// <exception cref="ContainerBackendUnavailableException">
	/// No backend is registered, the requested backend is not registered, or no registered backend is usable.
	/// The message carries each backend's diagnostics.
	/// </exception>
	public static async Task<IContainerBackend> ResolveAsync(CancellationToken cancellationToken = default)
	{
		var cached = Resolution;
		if (cached is not null)
		{
			return await cached.ConfigureAwait(false);
		}

		Task<IContainerBackend> resolution;
		lock (Sync)
		{
			resolution = Resolution ??= ResolveCoreAsync(PinnedBackend, Selection, [.. Registered], cancellationToken);
		}

		try
		{
			return await resolution.ConfigureAwait(false);
		}
		catch
		{
			// Do not cache a failed resolution: a host that installs a backend while the process runs, or a
			// test that registers one after a failure, must be able to resolve again.
			lock (Sync)
			{
				if (ReferenceEquals(Resolution, resolution))
				{
					Resolution = null;
				}
			}

			throw;
		}
	}

	/// <summary>
	/// Probes every registered backend and reports its availability, version and missing components.
	/// Intended for diagnostics and for callers that want to apply their own policy.
	/// </summary>
	public static async Task<IReadOnlyList<ContainerBackendInfo>> ProbeAllAsync(
		CancellationToken cancellationToken = default
	)
	{
		List<ContainerBackendInfo> probes = new();
		foreach (var backend in All)
		{
			probes.Add(await ProbeAsync(backend, cancellationToken).ConfigureAwait(false));
		}

		return probes;
	}

	/// <summary>Clears every registration, every selection and the cached resolution. Intended for tests.</summary>
	public static void Reset()
	{
		lock (Sync)
		{
			Registered.Clear();
			PinnedBackend = null;
			PinnedSelection = null;
			Resolution = null;
		}
	}

	static async Task<IContainerBackend> ResolveCoreAsync(
		IContainerBackend? pinned,
		ContainerBackendSelection selection,
		IContainerBackend[] registered,
		CancellationToken cancellationToken
	)
	{
		if (registered.Length == 0)
		{
			throw new ContainerBackendUnavailableException(
				"No container backend is registered. Reference a backend package (for example "
					+ "'Purview.Containers.Wsl' on Windows or 'Purview.Containers.Docker' with a reachable "
					+ "Docker daemon) or register one explicitly with ContainerBackends.Register(...)."
			);
		}

		if (pinned is not null)
		{
			var pinnedInfo = await ProbeAsync(pinned, cancellationToken).ConfigureAwait(false);
			if (!pinnedInfo.IsUsable)
			{
				throw new ContainerBackendUnavailableException(
					$"The pinned container backend '{pinned.Name}' is not usable: {Describe(pinnedInfo)}."
				);
			}

			return pinned;
		}

		if (!selection.IsAuto)
		{
			var named =
				registered.FirstOrDefault(backend =>
					string.Equals(backend.Name, selection.Name, StringComparison.OrdinalIgnoreCase)
				)
				?? throw new ContainerBackendUnavailableException(
					$"The container backend '{selection.Name}' is not registered. Registered backends: "
						+ $"{string.Join(", ", registered.Select(backend => backend.Name))}."
				);

			var namedInfo = await ProbeAsync(named, cancellationToken).ConfigureAwait(false);
			if (!namedInfo.IsUsable)
			{
				throw new ContainerBackendUnavailableException(
					$"The selected container backend '{named.Name}' is not usable: {Describe(namedInfo)}."
				);
			}

			return named;
		}

		StringBuilder report = new("No usable container backend was found (selection: auto).");
		foreach (var backend in registered)
		{
			var info = await ProbeAsync(backend, cancellationToken).ConfigureAwait(false);
			report.Append(Environment.NewLine).Append("  ").Append(Describe(info));
			if (info.IsUsable)
			{
				return backend;
			}
		}

		report
			.Append(Environment.NewLine)
			.Append("Install or fix a backend, or set ")
			.Append(SelectionEnvironmentVariable)
			.Append(" to one of: ")
			.Append(string.Join(", ", registered.Select(backend => backend.Name)))
			.Append('.');
		throw new ContainerBackendUnavailableException(report.ToString());
	}

	static async Task<ContainerBackendInfo> ProbeAsync(IContainerBackend backend, CancellationToken cancellationToken)
	{
		try
		{
			return await backend.GetInfoAsync(cancellationToken).ConfigureAwait(false)
				?? Unavailable(backend.Name, "the backend reported no information");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return Unavailable(backend.Name, $"{exception.GetType().Name}: {exception.Message}");
		}
	}

	static ContainerBackendInfo Unavailable(string name, string reason) =>
		new(name, IsAvailable: false, IsCompatible: false, Version: string.Empty, MissingComponents: [reason]);

	static string Describe(ContainerBackendInfo info)
	{
		if (!info.IsAvailable)
		{
			return $"{info.Name}: unavailable ({string.Join("; ", info.MissingComponents)})";
		}

		if (!info.IsCompatible)
		{
			return $"{info.Name}: incompatible, version {info.Version} "
				+ $"({string.Join("; ", info.MissingComponents)})";
		}

		return $"{info.Name}: available, version {info.Version}";
	}
}
