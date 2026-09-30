using System.Diagnostics;

namespace Purview.WslContainers;

[Explicit]
public class PerformanceTests
{
	[Test]
	[NotInParallel]
	public async Task WarmContainerStart_IsFast(CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		// Warm the image into the shared session store.
		await using (
			var warm = new ContainerBuilder().WithImage("alpine:latest").WithCommand("/bin/echo", "warm").Build()
		)
		{
			await warm.StartAsync(cancellationToken);
		}

		var stopwatch = Stopwatch.StartNew();
		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/echo", "hi")
			.Build();
		await container.StartAsync(cancellationToken);
		stopwatch.Stop();

		await TestContext.Current!.OutputWriter.WriteLineAsync(
			$"warm container start: {stopwatch.Elapsed}",
			cancellationToken
		);
		await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(60));
	}
}
