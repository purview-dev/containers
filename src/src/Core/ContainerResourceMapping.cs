namespace Purview.Containers;

/// <summary>A host file or directory copied to an absolute container path before startup.</summary>
public sealed record ContainerResourceMapping(string Source, string Target)
{
	/// <summary>
	/// Optional Unix mode for a file source. Most images accept the mode inherited from the host; a
	/// credential file such as an Erlang cookie must be owner-read-only (400).
	/// </summary>
	public UnixFileMode? Mode { get; init; }
}
