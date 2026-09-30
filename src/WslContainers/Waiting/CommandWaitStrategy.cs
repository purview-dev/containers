using System.Diagnostics.CodeAnalysis;

namespace Purview.WslContainers;

/// <summary>Waits until a command executed inside the container exits with the expected code (default 0).</summary>
[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Exec transient failures during startup are treated as not-ready."
)]
public sealed class CommandWaitStrategy : WaitStrategy
{
	private readonly string[] command;
	private int expectedExitCode;

	public CommandWaitStrategy(string[] command, int expectedExitCode = 0)
	{
		ArgumentNullException.ThrowIfNull(command);
		this.command = command;
		this.expectedExitCode = expectedExitCode;
	}

	/// <summary>Sets the expected exit code. Defaults to 0.</summary>
	public CommandWaitStrategy ForExitCode(int exitCode)
	{
		expectedExitCode = exitCode;
		return this;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		try
		{
			ExecResult result = await context
				.Container.ExecAsync(command, options: null, cancellationToken)
				.ConfigureAwait(false);
			return result.ExitCode == expectedExitCode;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return false;
		}
	}
}
