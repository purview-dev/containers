using Purview.Containers.Waiting;

namespace Purview.Containers.Wsl;

[Explicit]
public class SessionSweepTests
{
	[Test]
	public async Task SweepOrphanedSessions_LeavesTheCurrentSessionRunning(CancellationToken cancellationToken)
	{
		await WslcTest.SkipIfUnavailableAsync();

		var session = await WslContainerRuntime.Instance.GetSessionAsync(cancellationToken);
		await Assert.That(session.Name).StartsWith("purview-");
		var result = await WslContainerRuntime.Instance.SweepOrphanedSessionsAsync(cancellationToken);

		await Assert.That(result.TerminatedSessions).DoesNotContain(session.Name);

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/echo", "sweep-ok")
			.WithWaitStrategy(Wait.ForLogMessage("sweep-ok"))
			.Build();
		await container.StartAsync(cancellationToken);
		await Assert.That(container.State).IsNotEqualTo(ContainerState.Invalid);
	}
}
