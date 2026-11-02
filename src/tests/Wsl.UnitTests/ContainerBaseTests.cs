namespace Purview.Containers.Wsl;

public class ContainerBaseTests
{
	[Test]
	public async Task DisposeAsync_DuringStartup_CancelsStartupBeforeWaitingForCleanup(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		var disposed = false;
		await using var container = CreateContainer(
			async token =>
			{
				entered.TrySetResult();
				await Task.Delay(Timeout.InfiniteTimeSpan, token);
			},
			() => disposed = true
		);
		var startup = container.StartAsync(cancellationToken);
		await entered.Task.WaitAsync(cancellationToken);

		// Act
		await container.DisposeAsync();

		// Assert
		await Assert.That(() => startup).Throws<OperationCanceledException>();
		await Assert.That(disposed).IsTrue();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability",
		"CA2000",
		Justification = "ContainerBase takes ownership of the fake returned by the backend and disposes it."
	)]
	static Container CreateContainer(Func<CancellationToken, Task> starting, Action disposing) =>
		new ContainerBuilder(new FakeBackend(new FakeContainer { Starting = starting, Disposing = disposing }))
			.WithImage("fake:latest")
			.WithStartupTimeout(TimeSpan.FromSeconds(5))
			.WithCleanupTimeout(TimeSpan.FromMilliseconds(500))
			.Build();

	sealed class FakeBackend(IContainer container) : IContainerBackend
	{
		public string Name => "fake";

		public IContainer CreateContainer(IContainerConfiguration configuration) => container;

		public Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(new ContainerBackendInfo(Name, true, true, string.Empty, []));
	}
}
