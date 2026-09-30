using Purview.WslContainers.Runtime;
using Purview.WslContainers.Waiting;

namespace Purview.WslContainers;

public class WaitStrategyRunnerTests
{
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
		catch (WslContainerTimeoutException ex)
		{
			thrown = ex;
		}

		await Assert.That(thrown).IsNotNull();
		await Assert.That(thrown!.Message).Contains("runner-test");
		await Assert.That(thrown.Message).Contains("some service output");
	}
}
