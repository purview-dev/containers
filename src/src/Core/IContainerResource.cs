namespace Purview.Containers;

/// <summary>A backend-owned network or built image. Dispose after its containers.</summary>
public interface IContainerResource : IAsyncDisposable
{
	/// <summary>The network name or image reference to pass to a container builder.</summary>
	string Name { get; }

	/// <summary>Deletes the resource. Containers using it must be disposed first.</summary>
	Task DeleteAsync(CancellationToken cancellationToken = default);
}
