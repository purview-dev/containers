using Purview.WslContainers.Containers;

namespace Purview.WslContainers.Waiting;

/// <summary>Waits until the container init process is running.</summary>
public sealed class ContainerRunningWaitStrategy : WaitStrategy
{
	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		await Task.Yield();
		return context.Container.State == ContainerState.Running;
	}
}
