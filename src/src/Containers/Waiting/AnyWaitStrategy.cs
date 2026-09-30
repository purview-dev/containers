namespace Purview.Containers.Waiting;

/// <summary>Composes strategies; ready when any contained strategy is ready on the same check.</summary>
public sealed class AnyWaitStrategy : WaitStrategy
{
	readonly IReadOnlyList<IWaitStrategy> _strategies;

	public AnyWaitStrategy(IReadOnlyList<IWaitStrategy> strategies)
	{
		ArgumentNullException.ThrowIfNull(strategies);
		_strategies = strategies;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		foreach (var strategy in _strategies)
		{
			if (await strategy.UntilAsync(context, cancellationToken).ConfigureAwait(false))
			{
				return true;
			}
		}

		return false;
	}
}
