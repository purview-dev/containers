namespace Purview.WslContainers;

/// <summary>Composes strategies; ready only when all contained strategies are ready on the same check.</summary>
public sealed class AllWaitStrategy : WaitStrategy
{
	private readonly IReadOnlyList<IWaitStrategy> strategies;

	public AllWaitStrategy(IReadOnlyList<IWaitStrategy> strategies)
	{
		ArgumentNullException.ThrowIfNull(strategies);
		this.strategies = strategies;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		foreach (IWaitStrategy strategy in strategies)
		{
			if (!await strategy.UntilAsync(context, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}
		}

		return true;
	}
}
