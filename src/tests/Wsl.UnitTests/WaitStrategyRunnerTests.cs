using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.Wsl;

public class WaitStrategyRunnerTests
{
	[Test]
	public async Task RunAsync_PredicateIgnoresCancellation_StillHonoursDeadline(CancellationToken cancellationToken)
	{
		// Arrange
		await using FakeContainer container = new() { Name = "uncooperative" };
		WaitContext context = new(container, container.PortMappings, networkIp: null);
		TaskCompletionSource<bool> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
		var strategy = Wait.ForCustom((_, _) => never.Task).WithTimeout(TimeSpan.FromMilliseconds(100));

		// Act / Assert
		await Assert
			.That(() =>
				WaitStrategyRunner
					.RunAsync(context, [strategy], TimeSpan.FromSeconds(1), cancellationToken)
					.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
			)
			.Throws<ContainerTimeoutException>();
	}

	[Test]
	public async Task RunAsync_CallerCancels_PropagatesCancellation(CancellationToken cancellationToken)
	{
		// Arrange
		await using FakeContainer container = new();
		WaitContext context = new(container, container.PortMappings, networkIp: null);
		TaskCompletionSource<bool> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
		var strategy = Wait.ForCustom((_, _) => never.Task);
		using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancelled.CancelAfter(TimeSpan.FromMilliseconds(100));

		// Act / Assert
		await Assert
			.That(() => WaitStrategyRunner.RunAsync(context, [strategy], TimeSpan.FromMinutes(1), cancelled.Token))
			.Throws<OperationCanceledException>();
	}

	[Test]
	public async Task RunAsync_SucceedsWhenConditionEventuallyTrue(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new() { Name = "runner-test" };
		WaitContext context = new(container, container.PortMappings, networkIp: null);
		var calls = 0;
		IWaitStrategy strategy = Wait.ForCustom((_, _) => Task.FromResult(++calls >= 3))
			.WithInterval(TimeSpan.FromMilliseconds(10));

		await WaitStrategyRunner.RunAsync(context, new[] { strategy }, TimeSpan.FromSeconds(5), cancellationToken);

		await Assert.That(calls).IsGreaterThanOrEqualTo(3);
	}

	[Test]
	public async Task RunAsync_TimesOut_ThrowsWithDiagnostics(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new()
		{
			Name = "runner-test",
			Logs = "some service output\n",
			PortMappings = new Dictionary<ushort, ushort> { [80] = 1234 },
		};
		WaitContext context = new(container, container.PortMappings, networkIp: null);
		IWaitStrategy strategy = Wait.ForCustom((_, _) => Task.FromResult(false))
			.WithInterval(TimeSpan.FromMilliseconds(10))
			.WithTimeout(TimeSpan.FromMilliseconds(300));

		Exception? thrown = null;
		try
		{
			await WaitStrategyRunner.RunAsync(context, new[] { strategy }, TimeSpan.FromSeconds(5), cancellationToken);
		}
		catch (ContainerTimeoutException ex)
		{
			thrown = ex;
		}

		await Assert.That(thrown).IsNotNull();
		await Assert.That(thrown!.Message).Contains("runner-test");
		await Assert.That(thrown.Message).Contains("some service output");
	}
}
