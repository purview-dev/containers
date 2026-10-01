namespace Purview.Containers.Wsl;

/// <summary>The process-wide WSL Containers runtime. Owns the shared WSLC session.</summary>
public interface IContainerRuntime : IAsyncDisposable
{
	/// <summary>Reports WSLC availability, version and missing components.</summary>
	Task<WslContainerRuntimeInfo> GetInfoAsync(CancellationToken cancellationToken = default);

	/// <summary>Returns the (shared, lazily started) session, creating it on first use.</summary>
	Task<IContainerSession> GetSessionAsync(CancellationToken cancellationToken = default);
}
