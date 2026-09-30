using System.Net;
using Purview.WslContainers.Containers;
using Purview.WslContainers.Runtime;
using Purview.WslContainers.Waiting;

namespace Purview.WslContainers;

[Explicit]
public class WaitStrategyTests
{
	[Test]
	public async Task LogMessage_WaitsForReadiness()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sh", "-c", "echo ready; sleep 300")
			.WithWaitStrategy(Wait.ForLogMessage("ready"))
			.Build();

		await container.StartAsync();

		await Assert.That(container.State).IsEqualTo(ContainerState.Running);
	}

	[Test]
	public async Task TcpPort_WaitsForListener()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.WithWaitStrategy(Wait.ForTcpPort(8080))
			.Build();

		await container.StartAsync();

		var hostPort = container.GetMappedPublicPort(8080);
		await Assert.That(hostPort).IsGreaterThan((ushort)0);
	}

	[Test]
	public async Task Http_WaitsForExpectedStatus()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.WithWaitStrategy(Wait.ForHttp("/").ForPort(8080).ForStatusCode(HttpStatusCode.OK))
			.Build();

		await container.StartAsync();
	}

	[Test]
	public async Task Command_WaitsForExitCodeZero()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sleep", "300")
			.WithWaitStrategy(Wait.ForCommand("/bin/true"))
			.Build();

		await container.StartAsync();

		await Assert.That(container.State).IsEqualTo(ContainerState.Running);
	}

	[Test]
	public async Task Composition_AllStrategiesReady()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("python:3-alpine")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithCommand("/bin/sh", "-c", "python3 -m http.server 8080")
			.WithWaitStrategy(
				Wait.ForAll(Wait.ForTcpPort(8080), Wait.ForCommand("python3", "-c", "import sys; sys.exit(0)"))
			)
			.Build();

		await container.StartAsync();
	}

	[Test]
	public async Task Timeout_ThrowsWithDiagnosticsAndIsDisposable()
	{
		await WslcTest.SkipIfUnavailableAsync();

		var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sh", "-c", "echo booting; sleep 300")
			.WithWaitStrategy(
				Wait.ForLogMessage("this message never appears")
					.WithTimeout(TimeSpan.FromSeconds(3))
					.WithInterval(TimeSpan.FromMilliseconds(100))
			)
			.Build();

		WslContainerTimeoutException? thrown = null;
		try
		{
			await container.StartAsync();
		}
		catch (WslContainerTimeoutException ex)
		{
			thrown = ex;
		}

		await Assert.That(thrown).IsNotNull();
		await Assert.That(thrown!.Message).Contains(container.Name);
		await Assert.That(thrown.Message).Contains("booting");

		await container.DisposeAsync();
	}
}
