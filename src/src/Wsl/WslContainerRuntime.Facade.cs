namespace Purview.Containers.Wsl;

/// <summary>
/// The process-wide WSL Containers runtime as seen from a platform-neutral (<c>net10.0</c>) consumer.
/// </summary>
/// <remarks>
/// <para>
/// The facade forwards host diagnostics to the Windows implementation loaded from the payload and
/// reports "unavailable" when this host cannot run WSLC. It exists mainly for the documented host
/// check (<c>WslContainerRuntime.Instance.GetInfoAsync()</c>); container work always goes through the
/// backend-neutral builders.
/// </para>
/// <para>
/// Session-level access (<see cref="GetSessionAsync" />) is not exposed from the facade, because the
/// WSLC session handle is a Windows-only type. A Windows-targeting consumer keeps the full runtime.
/// </para>
/// </remarks>
public sealed class WslContainerRuntime : IContainerRuntime
{
	/// <summary>
	/// Environment variable that overrides the session storage path for the Windows implementation.
	/// </summary>
	public const string StoragePathEnvironmentVariable = "PURVIEW_CONTAINERS_STORAGE_PATH";

	/// <summary>The pre-rename storage path variable, still honoured as a fallback.</summary>
	public const string LegacyStoragePathEnvironmentVariable = "WSL_CONTAINERS_STORAGE_PATH";

	static readonly Lazy<WslContainerRuntime> InstanceHolder = new(() => new WslContainerRuntime());

	/// <summary>The default process-wide runtime.</summary>
	public static WslContainerRuntime Instance => InstanceHolder.Value;

	/// <summary>Creates a runtime with default options.</summary>
	public WslContainerRuntime() { }

	/// <summary>Creates a runtime with the given options (retained for API parity; applied when WSLC runs).</summary>
	public WslContainerRuntime(WslContainerRuntimeOptions options) =>
		Options = options ?? WslContainerRuntimeOptions.Default;

	/// <summary>The options this runtime was created with, if any.</summary>
	public WslContainerRuntimeOptions? Options { get; }

	/// <inheritdoc />
	public async Task<WslContainerRuntimeInfo> GetInfoAsync(CancellationToken cancellationToken = default)
	{
		var backend = WslPayload.TryCreateBackend();
		if (backend is null)
		{
			return new WslContainerRuntimeInfo(
				Version: string.Empty,
				MissingComponents: [WslPayload.FailureReason],
				IsAvailable: false,
				IsCompatible: false
			);
		}

		var info = await backend.GetInfoAsync(cancellationToken).ConfigureAwait(false);
		return new WslContainerRuntimeInfo(info.Version, info.MissingComponents, info.IsAvailable, info.IsCompatible);
	}

	/// <inheritdoc />
	public Task<IContainerSession> GetSessionAsync(CancellationToken cancellationToken = default) =>
		throw new WslContainerPrerequisiteException(
			"Direct WSLC session access is only available from a Windows target framework "
				+ "(net10.0-windows10.0.19041.0 or later). Use the backend-neutral container API instead."
		);

	/// <inheritdoc />
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
