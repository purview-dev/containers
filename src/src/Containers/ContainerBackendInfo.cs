namespace Purview.Containers;

/// <summary>
/// Availability and version information reported by a container backend. Used by diagnostics and by the
/// backend selection policy: a backend is selectable when it is both available and compatible.
/// </summary>
/// <param name="Name">Backend identifier, matching <see cref="IContainerBackend.Name" />.</param>
/// <param name="IsAvailable">True when every component the backend needs is installed.</param>
/// <param name="IsCompatible">True when the installed components are compatible with this library.</param>
/// <param name="Version">Reported backend version, or an empty string when unknown.</param>
/// <param name="MissingComponents">Components that are missing or need an update.</param>
public sealed record ContainerBackendInfo(
	string Name,
	bool IsAvailable,
	bool IsCompatible,
	string Version,
	IReadOnlyList<string> MissingComponents
)
{
	/// <summary>True when the backend can be selected.</summary>
	public bool IsUsable => IsAvailable && IsCompatible;
}
