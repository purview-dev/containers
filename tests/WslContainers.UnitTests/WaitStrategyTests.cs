using System.Text.RegularExpressions;
using Purview.WslContainers;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.UnitTests;

public class WaitStrategyTests
{
	private static WaitContext Context(FakeContainer container)
	{
		return new WaitContext(container, container.PortMappings, networkIp: null);
	}

	[Test]
	public async Task LogMessage_MatchesAccumulatedOutput()
	{
		FakeContainer container = new FakeContainer
		{
			Logs = "2026-01-01 database system is ready to accept connections\n",
		};
		IWaitStrategy strategy = Wait.ForLogMessage("database system is ready");

		await Assert.That(await strategy.UntilAsync(Context(container), CancellationToken.None)).IsTrue();
	}

	[Test]
	public async Task LogMessage_DoesNotMatchAbsentMessage()
	{
		FakeContainer container = new FakeContainer { Logs = "starting up\n" };
		IWaitStrategy strategy = Wait.ForLogMessage(new Regex("ready to accept"));

		await Assert.That(await strategy.UntilAsync(Context(container), CancellationToken.None)).IsFalse();
	}

	[Test]
	public async Task Command_ReturnsTrueOnExitCodeZero()
	{
		FakeContainer container = new FakeContainer { Exec = _ => new ExecResult(0, "ok", string.Empty) };
		IWaitStrategy strategy = Wait.ForCommand("/bin/true");

		await Assert.That(await strategy.UntilAsync(Context(container), CancellationToken.None)).IsTrue();
	}

	[Test]
	public async Task Command_ReturnsFalseOnNonZeroExit()
	{
		FakeContainer container = new FakeContainer { Exec = _ => new ExecResult(1, string.Empty, "boom") };
		IWaitStrategy strategy = Wait.ForCommand("/bin/false");

		await Assert.That(await strategy.UntilAsync(Context(container), CancellationToken.None)).IsFalse();
	}

	[Test]
	public async Task Command_ReturnsFalseWhenExecThrows()
	{
		FakeContainer container = new FakeContainer
		{
			Exec = _ => throw new InvalidOperationException("container not ready"),
		};
		IWaitStrategy strategy = Wait.ForCommand("/bin/true");

		await Assert.That(await strategy.UntilAsync(Context(container), CancellationToken.None)).IsFalse();
	}

	[Test]
	public async Task ForAll_RequiresEveryStrategy()
	{
		FakeContainer container = new FakeContainer();
		IWaitStrategy all = Wait.ForAll(
			Wait.ForCustom((_, _) => Task.FromResult(true)),
			Wait.ForCustom((_, _) => Task.FromResult(false))
		);
		IWaitStrategy any = Wait.ForAny(
			Wait.ForCustom((_, _) => Task.FromResult(true)),
			Wait.ForCustom((_, _) => Task.FromResult(false))
		);

		await Assert.That(await all.UntilAsync(Context(container), CancellationToken.None)).IsFalse();
		await Assert.That(await any.UntilAsync(Context(container), CancellationToken.None)).IsTrue();
	}

	[Test]
	public async Task ContainerRunning_ReflectsState()
	{
		FakeContainer running = new FakeContainer { State = ContainerState.Running };
		FakeContainer exited = new FakeContainer { State = ContainerState.Exited };
		IWaitStrategy strategy = Wait.ForContainerRunning();

		await Assert.That(await strategy.UntilAsync(Context(running), CancellationToken.None)).IsTrue();
		await Assert.That(await strategy.UntilAsync(Context(exited), CancellationToken.None)).IsFalse();
	}

	[Test]
	public async Task TcpPort_ReturnsFalseWhenNoMapping()
	{
		FakeContainer container = new FakeContainer();
		IWaitStrategy strategy = Wait.ForTcpPort(8080);

		await Assert.That(await strategy.UntilAsync(Context(container), CancellationToken.None)).IsFalse();
	}
}
