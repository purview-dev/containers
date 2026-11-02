namespace Purview.Containers.Wsl;

sealed class WslResource(WslContainerSession session, string name, bool network, TimeSpan timeout) : IContainerResource
{
	int _disposed;
	public string Name => name;

	public Task DeleteAsync(CancellationToken cancellationToken = default) =>
		session.RunCommandAsync(network ? ["network", "remove", Name] : ["rmi", Name], timeout, cancellationToken);

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
			return;
		await DeleteAsync().ConfigureAwait(false);
	}
}
