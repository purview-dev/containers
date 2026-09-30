namespace Purview.Containers.Waiting;

/// <summary>Composes strategies; ready only when all contained strategies are ready on the same check.</summary>
public sealed class AllWaitStrategy : WaitStrategy
{
	readonly IReadOnlyList<IWaitStrategy> _strategies;

	public AllWaitStrategy(IReadOnlyList<IWaitStrategy> strategies)
	{
		ArgumentNullException.ThrowIfNull(strategies);
		_strategies = strategies;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		foreach (var strategy in _strategies)
		{
			if (!await strategy.UntilAsync(context, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}
		}

		return true;
	}
}
