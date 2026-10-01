namespace Purview.Containers.Waiting;

/// <summary>Waits until a custom predicate returns <c>true</c>.</summary>
public sealed class CustomWaitStrategy : WaitStrategy
{
	readonly Func<WaitContext, CancellationToken, Task<bool>> _predicate;

	public CustomWaitStrategy(Func<WaitContext, CancellationToken, Task<bool>> predicate)
	{
		ArgumentNullException.ThrowIfNull(predicate);
		_predicate = predicate;
	}

	/// <inheritdoc />
	public override Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		return _predicate(context, cancellationToken);
	}
}
