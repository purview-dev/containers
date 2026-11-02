using DotNet.Testcontainers;

namespace Purview.Containers.Docker;

sealed class DockerResource(string name, IFutureResource resource, IAsyncDisposable disposable, TimeSpan timeout)
	: IContainerResource
{
	int _disposed;

	public string Name => name;

	public async Task DeleteAsync(CancellationToken cancellationToken = default)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(timeout);
		await resource.DeleteAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
			return;
		await disposable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
	}
}
