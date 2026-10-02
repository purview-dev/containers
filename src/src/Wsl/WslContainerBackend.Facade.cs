namespace Purview.Containers.Wsl;

/// <summary>
/// The WSL Containers (WSLC) backend as seen from a platform-neutral (<c>net10.0</c>) consumer.
/// </summary>
/// <remarks>
/// <para>
/// On a Windows host this facade loads the Windows build of the backend from the local payload and
/// delegates to it; the container it returns is a real WSLC-backed <see cref="IContainer" />. On any
/// other host - or when the payload is absent - it reports <c>wsl</c> as unavailable, so
/// <see cref="ContainerBackends" /> auto-selection moves on to the next registered backend (Docker).
/// </para>
/// <para>
/// A Windows-targeting consumer never sees this type: it binds the Windows build of the assembly,
/// where <c>WslContainerBackend</c> is the implementation itself.
/// </para>
/// </remarks>
public sealed class WslContainerBackend : IContainerBackend, IContainerBackendPreference
{
	readonly WslContainerRuntimeOptions? _options;

	/// <summary>Creates the backend over the default process-wide runtime.</summary>
	public WslContainerBackend() { }

	/// <summary>
	/// Creates the backend configured with the given runtime options. On a platform-neutral target the
	/// options are forwarded to the Windows implementation when it is loaded.
	/// </summary>
	public WslContainerBackend(WslContainerRuntimeOptions options)
	{
		_options = options ?? WslContainerRuntimeOptions.Default;
	}

	/// <summary>Stable backend identifier.</summary>
	public string Name => "wsl";

	/// <summary>
	/// Automatic selection preference: WSLC is preferred over Docker on a machine that can run both.
	/// </summary>
	public int AutoPriority => 0;

	/// <summary>Factory used by the generated backend registration.</summary>
	public static WslContainerBackend Create() => new();

	/// <inheritdoc />
	public IContainer CreateContainer(IContainerConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		return Resolve().CreateContainer(configuration);
	}

	/// <inheritdoc />
	public async Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default)
	{
		var backend = WslPayload.TryCreateBackend();
		if (backend is null)
		{
			return Unavailable(WslPayload.FailureReason);
		}

		try
		{
			return await backend.GetInfoAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return Unavailable($"{exception.GetType().Name}: {exception.Message}");
		}
	}

	IContainerBackend Resolve() =>
		WslPayload.TryCreateBackend(_options)
		?? throw new WslContainerPrerequisiteException(
			$"The WSL Containers backend cannot run here. {WslPayload.FailureReason}"
		);

	static ContainerBackendInfo Unavailable(string reason) =>
		new("wsl", IsAvailable: false, IsCompatible: false, Version: string.Empty, MissingComponents: [reason]);
}
