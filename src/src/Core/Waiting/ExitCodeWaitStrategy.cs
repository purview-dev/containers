namespace Purview.Containers.Waiting;

sealed class ExitCodeWaitStrategy(long expectedExitCode) : WaitStrategy
{
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		if (context.Container.State != ContainerState.Exited)
		{
			return false;
		}

		var actual = await context.Container.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
		if (actual != expectedExitCode)
			throw new Runtime.ContainerExitCodeException(expectedExitCode, actual);
		return true;
	}
}
