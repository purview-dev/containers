using Purview.WslContainers;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.IntegrationTests;

public class AlpineLifecycleTests
{
	[Test]
	public async Task EchoContainer_ProducesOutputAndCleansUp()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/echo", "hello-from-alpine")
			.Build();

		await container.StartAsync();

		await Assert.That(container.State).IsNotEqualTo(ContainerState.Invalid);
		string logs = await container.GetLogsAsync();
		await Assert.That(logs).Contains("hello-from-alpine");
	}

	[Test]
	public async Task EnvironmentVariable_IsPassedToInitProcess()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithEnvironment("GREETING", "hello-env")
			.WithCommand("/bin/sh", "-c", "echo $GREETING")
			.Build();

		await container.StartAsync();

		string logs = await container.GetLogsAsync();
		await Assert.That(logs).Contains("hello-env");
	}

	[Test]
	public async Task StopAsync_ThenDisposeAsync_IsIdempotent()
	{
		await WslcTest.SkipIfUnavailableAsync();

		WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sleep", "300")
			.Build();

		await container.StartAsync();
		await Assert.That(container.State).IsEqualTo(ContainerState.Running);

		await container.StopAsync();
		await Assert.That(container.State).IsEqualTo(ContainerState.Exited);

		await container.DisposeAsync();
		await container.DisposeAsync();
		await Assert.That(container.State).IsEqualTo(ContainerState.Exited);
	}

	[Test]
	public async Task GetMappedPublicPort_BeforeStart_Throws()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using WslContainer container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/echo", "hi")
			.Build();

		await Assert.That(() => container.GetMappedPublicPort(6379)).Throws<InvalidOperationException>();
	}
}
