using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.Wsl;

[Explicit]
public class CleanupTests
{
	[Test]
	public async Task Dispose_AfterWaitTimeout_IsIdempotent()
	{
		await WslcTest.SkipIfUnavailableAsync();

		var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sh", "-c", "echo booting; sleep 300")
			.WithWaitStrategy(Wait.ForLogMessage("this never appears").WithTimeout(TimeSpan.FromSeconds(3)))
			.Build();

		await Assert.That(() => container.StartAsync()).Throws<ContainerTimeoutException>();
		await container.DisposeAsync();
		await container.DisposeAsync();
	}

	[Test]
	public async Task Dispose_AfterInvalidImage_DoesNotThrow()
	{
		await WslcTest.SkipIfUnavailableAsync();

		var container = new ContainerBuilder()
			.WithImage("docker.io/library/definitely-not-a-real-image-xyz:latest")
			.Build();

		await Assert.That(() => container.StartAsync()).Throws<Exception>();
		await container.DisposeAsync();
	}

	[Test]
	public async Task Stop_ThenDispose_IsIdempotent()
	{
		await WslcTest.SkipIfUnavailableAsync();

		var container = new ContainerBuilder().WithImage("alpine:latest").WithCommand("/bin/sleep", "300").Build();

		await container.StartAsync();
		await container.StopAsync();
		await container.DisposeAsync();
		await container.DisposeAsync();
	}

	[Test]
	public async Task ImmediateExit_ThenDispose_Works()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/echo", "bye")
			.Build();

		await container.StartAsync();
		await container.DisposeAsync();
	}
}
