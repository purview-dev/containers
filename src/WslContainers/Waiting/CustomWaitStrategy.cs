namespace Purview.WslContainers;

/// <summary>Waits until a custom predicate returns <c>true</c>.</summary>
public sealed class CustomWaitStrategy : WaitStrategy
{
	private readonly Func<WaitContext, CancellationToken, Task<bool>> predicate;

	public CustomWaitStrategy(Func<WaitContext, CancellationToken, Task<bool>> predicate)
	{
		ArgumentNullException.ThrowIfNull(predicate);
		this.predicate = predicate;
	}

	/// <inheritdoc />
	public override Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		return predicate(context, cancellationToken);
	}
}
