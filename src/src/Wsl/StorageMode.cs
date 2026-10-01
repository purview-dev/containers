namespace Purview.Containers.Wsl;

/// <summary>How the session storage path (and therefore the image store) is scoped.</summary>
public enum StorageMode
{
	/// <summary>
	/// All sessions share one stable storage directory, so images are pulled once and reused across
	/// process runs. Concurrent sessions that collide on the shared store fall back to an isolated store.
	/// </summary>
	Shared = 0,

	/// <summary>Each session gets a unique, isolated storage directory (fresh image store per process).</summary>
	PerSession = 1,
}
