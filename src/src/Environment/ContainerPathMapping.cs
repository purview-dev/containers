namespace Purview.Containers.Environment;

/// <summary>Maps paths beneath a client root without matching similarly named sibling directories.</summary>
public sealed class ContainerPathMapping
{
	readonly string _clientRoot;
	readonly string _daemonRoot;

	/// <summary>Creates a mapping. Daemon paths are interpreted by the daemon, not by the client OS.</summary>
	public ContainerPathMapping(string clientRoot, string daemonRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(clientRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(daemonRoot);
		_clientRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(clientRoot));
		_daemonRoot = daemonRoot.TrimEnd('/', '\\');
		if (_daemonRoot.Length == 0)
			_daemonRoot = "/";
	}

	internal int Specificity => _clientRoot.Length;

	/// <summary>Returns a mapped path, or <see langword="null"/> when the path is outside this root.</summary>
	public string? Map(string clientPath)
	{
		var relative = Path.GetRelativePath(_clientRoot, Path.GetFullPath(clientPath));
		if (
			Path.IsPathRooted(relative)
			|| relative == ".."
			|| relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
		)
			return null;

		return relative == "."
			? _daemonRoot
			: _daemonRoot.TrimEnd('/') + "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
	}
}
