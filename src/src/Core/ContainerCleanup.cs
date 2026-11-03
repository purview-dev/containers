namespace Purview.Containers;

/// <summary>
/// Process-wide cleanup defaults shared by every backend.
/// </summary>
/// <remarks>
/// Backends record the named volumes they create. When <see cref="CleanUpVolumesOnExit" /> is enabled, a
/// backend deletes those volumes (and, where needed, the containers using them) when the process exits or
/// its session terminates. Volumes that already existed before the library used them are never deleted,
/// and neither the shared image store nor host bind mounts are touched.
/// </remarks>
public static class ContainerCleanup
{
	/// <summary>
	/// Environment variable that enables <see cref="CleanUpVolumesOnExit" />. Accepts <c>1</c> or
	/// <c>true</c>. It is read when this type is first used; set it before creating containers.
	/// </summary>
	public const string CleanUpVolumesEnvironmentVariable = "PURVIEW_CONTAINERS_CLEAN_VOLUMES";

	static bool CleanUpVolumesOnExitDefault = IsEnabled(
		Environment.GetEnvironmentVariable(CleanUpVolumesEnvironmentVariable)
	);

	/// <summary>
	/// Gets or sets whether backends delete the named volumes they created when the process exits or its
	/// session terminates. Defaults to the value of <see cref="CleanUpVolumesEnvironmentVariable" />.
	/// </summary>
	/// <remarks>
	/// Set it before creating containers: backends record volume ownership as containers are created.
	/// Resources that already existed are not owned and are left alone. The cleanup is best-effort and is
	/// also exposed explicitly through <see cref="ContainerBackends.CleanUpAsync" /> and each backend's
	/// <see cref="IContainerBackend.CleanUpAsync" />.
	/// </remarks>
	public static bool CleanUpVolumesOnExit
	{
		get => Volatile.Read(ref CleanUpVolumesOnExitDefault);
		set => Volatile.Write(ref CleanUpVolumesOnExitDefault, value);
	}

	static bool IsEnabled(string? value) =>
		value is not null && (value == "1" || (bool.TryParse(value, out var enabled) && enabled));
}
