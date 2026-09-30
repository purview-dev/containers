using Purview.WslContainers;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.IntegrationTests;

public class CleanupTests
{
	[Test]
	public async Task Dispose_AfterWaitTimeout_IsIdempotent()
	{
		await WslcTest.SkipIfUnavailableAsync();

		WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sh", "-c", "echo booting; sleep 300")
			.WithWaitStrategy(Wait.ForLogMessage("this never appears").WithTimeout(TimeSpan.FromSeconds(3)))
			.Build();

		await Assert.That(() => container.StartAsync()).Throws<WslContainerTimeoutException>();
		await container.DisposeAsync();
		await container.DisposeAsync();
	}

	[Test]
	public async Task Dispose_AfterInvalidImage_DoesNotThrow()
	{
		await WslcTest.SkipIfUnavailableAsync();

		WslContainer container = new ContainerBuilder()
			.WithImage("docker.io/library/definitely-not-a-real-image-xyz:latest")
			.Build();

		await Assert.That(() => container.StartAsync()).Throws<Exception>();
		await container.DisposeAsync();
	}

	[Test]
	public async Task Stop_ThenDispose_IsIdempotent()
	{
		await WslcTest.SkipIfUnavailableAsync();

		WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sleep", "300")
			.Build();

		await container.StartAsync();
		await container.StopAsync();
		await container.DisposeAsync();
		await container.DisposeAsync();
	}

	[Test]
	public async Task ImmediateExit_ThenDispose_Works()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/echo", "bye")
			.Build();

		await container.StartAsync();
		await container.DisposeAsync();
	}
}
