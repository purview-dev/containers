using System.Text.RegularExpressions;
using Purview.Containers.Waiting;

namespace Purview.Containers.Wsl;

public class WaitStrategyTests
{
	static WaitContext Context(FakeContainer container)
	{
		return new WaitContext(container, container.PortMappings, networkIp: null);
	}

	[Test]
	public async Task LogMessage_MatchesAccumulatedOutput(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new()
		{
			Logs = "2026-01-01 database system is ready to accept connections\n",
		};
		var strategy = Wait.ForLogMessage("database system is ready");

		await Assert.That(await strategy.UntilAsync(Context(container), cancellationToken)).IsTrue();
	}

	[Test]
	public async Task LogMessage_DoesNotMatchAbsentMessage(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new() { Logs = "starting up\n" };
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
		var strategy = Wait.ForLogMessage(new Regex("ready to accept"));
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.

		await Assert.That(await strategy.UntilAsync(Context(container), cancellationToken)).IsFalse();
	}

	[Test]
	public async Task Command_ReturnsTrueOnExitCodeZero(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new() { Exec = _ => new ExecResult(0, "ok", string.Empty) };
		var strategy = Wait.ForCommand("/bin/true");

		await Assert.That(await strategy.UntilAsync(Context(container), cancellationToken)).IsTrue();
	}

	[Test]
	public async Task Command_ReturnsFalseOnNonZeroExit(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new() { Exec = _ => new ExecResult(1, string.Empty, "boom") };
		var strategy = Wait.ForCommand("/bin/false");

		await Assert.That(await strategy.UntilAsync(Context(container), cancellationToken)).IsFalse();
	}

	[Test]
	public async Task Command_ReturnsFalseWhenExecThrows(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new()
		{
			Exec = _ => throw new InvalidOperationException("container not ready"),
		};
		var strategy = Wait.ForCommand("/bin/true");

		await Assert.That(await strategy.UntilAsync(Context(container), cancellationToken)).IsFalse();
	}

	[Test]
	public async Task ForAll_RequiresEveryStrategy(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new();
		var all = Wait.ForAll(
			Wait.ForCustom((_, _) => Task.FromResult(true)),
			Wait.ForCustom((_, _) => Task.FromResult(false))
		);
		var any = Wait.ForAny(
			Wait.ForCustom((_, _) => Task.FromResult(true)),
			Wait.ForCustom((_, _) => Task.FromResult(false))
		);

		await Assert.That(await all.UntilAsync(Context(container), cancellationToken)).IsFalse();
		await Assert.That(await any.UntilAsync(Context(container), cancellationToken)).IsTrue();
	}

	[Test]
	public async Task ContainerRunning_ReflectsState(CancellationToken cancellationToken)
	{
		await using FakeContainer running = new() { State = ContainerState.Running };
		await using FakeContainer exited = new() { State = ContainerState.Exited };
		var strategy = Wait.ForContainerRunning();

		await Assert.That(await strategy.UntilAsync(Context(running), cancellationToken)).IsTrue();
		await Assert.That(await strategy.UntilAsync(Context(exited), cancellationToken)).IsFalse();
	}

	[Test]
	public async Task TcpPort_ReturnsFalseWhenNoMapping(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new();
		var strategy = Wait.ForTcpPort(8080);

		await Assert.That(await strategy.UntilAsync(Context(container), cancellationToken)).IsFalse();
	}
}
