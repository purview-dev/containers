namespace Purview.Containers.Wsl;

/// <summary>Configuration for the WSLC session owned by a runtime.</summary>
public sealed record WslContainerRuntimeOptions
{
	/// <summary>CPU count for the session VM.</summary>
	public uint? CPUCount { get; init; }

	/// <summary>Memory (MB) for the session VM.</summary>
	public uint? MemorySizeInMB { get; init; }

	/// <summary>Expose GPU devices in the session.</summary>
	public bool EnableGPU { get; init; }

	/// <summary>
	/// Session name. Defaults to <c>wslc-{processId}-{random}</c>. Session names are machine-unique.
	/// </summary>
	public string? SessionName { get; init; }

	/// <summary>
	/// Storage path for the session VHD and image store. When unset and <see cref="StorageMode" /> is
	/// <see cref="StorageMode.Shared" />, the default shared image directory is used
	/// (<c>%LOCALAPPDATA%\Purview\WslContainers\images</c>). The
	/// <see cref="WslContainerRuntime.StoragePathEnvironmentVariable" /> environment variable overrides the
	/// default when this is unset; the pre-rename <c>WSL_CONTAINERS_STORAGE_PATH</c> is still honoured as a
	/// fallback.
	/// </summary>
	public string? StoragePath { get; init; }

	/// <summary>Storage scoping. Defaults to <see cref="StorageMode.Shared" /> so images are pulled once.</summary>
	public StorageMode StorageMode { get; init; } = StorageMode.Shared;

	/// <summary>Session idle timeout.</summary>
	public TimeSpan? SessionTimeout { get; init; }

	/// <summary>
	/// Overrides <see cref="ContainerCleanup.CleanUpVolumesOnExit" /> for this runtime. <c>true</c> deletes the
	/// named session volumes at termination, <c>false</c> retains them, and <c>null</c> uses the shared
	/// setting. Defaults to <c>null</c>.
	/// </summary>
	/// <remarks>
	/// Enable this - or set <c>PURVIEW_CONTAINERS_CLEAN_VOLUMES=true</c> before the first container is
	/// created - for test runs that must leave no volume state behind. Only volumes recorded for this
	/// session are deleted; the shared image store is never touched. Session termination already removes
	/// the session's containers, so this option adds the volume deletion that Testcontainers does not do.
	/// </remarks>
	public bool? CleanUpVolumesOnTerminate { get; init; }

	/// <summary>Disables the process-exit cleanup hook (mainly for testing).</summary>
	public bool DisableProcessExitCleanup { get; init; }

	/// <summary>
	/// Default options. The session VM is capped at 4096 MB so memory-hungry services such as SQL Server
	/// (which requires at least 2000 MB) start without extra configuration; lighter containers are unaffected.
	/// </summary>
	public static WslContainerRuntimeOptions Default { get; } = new() { MemorySizeInMB = 4096 };
}
