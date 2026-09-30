namespace Purview.WslContainers;

/// <summary>Composes strategies; ready when any contained strategy is ready on the same check.</summary>
public sealed class AnyWaitStrategy : WaitStrategy
{
	private readonly IReadOnlyList<IWaitStrategy> strategies;

	public AnyWaitStrategy(IReadOnlyList<IWaitStrategy> strategies)
	{
		ArgumentNullException.ThrowIfNull(strategies);
		this.strategies = strategies;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		foreach (IWaitStrategy strategy in strategies)
		{
			if (await strategy.UntilAsync(context, cancellationToken).ConfigureAwait(false))
			{
				return true;
			}
		}

		return false;
	}
}
