using System.Diagnostics;
using Purview.WslContainers;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.IntegrationTests;

public class PerformanceTests
{
	[Test]
	[NotInParallel]
	public async Task WarmContainerStart_IsFast()
	{
		await WslcTest.SkipIfUnavailableAsync();

		// Warm the image into the shared session store.
		await using (
			WslContainer warm = new ContainerBuilder()
				.WithImage("alpine:latest")
				.WithCommand("/bin/echo", "warm")
				.Build()
		)
		{
			await warm.StartAsync();
		}

		Stopwatch stopwatch = Stopwatch.StartNew();
		await using WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/echo", "hi")
			.Build();
		await container.StartAsync();
		stopwatch.Stop();

		TestContext.Current!.OutputWriter.WriteLine($"warm container start: {stopwatch.Elapsed}");
		await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(60));
	}
}
