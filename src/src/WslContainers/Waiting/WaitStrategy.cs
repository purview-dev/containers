namespace Purview.WslContainers.Waiting;

/// <summary>Base class for wait strategies with shared polling configuration.</summary>
public abstract class WaitStrategy : IWaitStrategy
{
	/// <inheritdoc />
	public TimeSpan? Timeout { get; protected set; }

	/// <inheritdoc />
	public TimeSpan Interval { get; protected set; } = TimeSpan.FromMilliseconds(250);

	/// <inheritdoc />
	public int? Retries { get; protected set; }

	/// <inheritdoc />
	public abstract Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken);

	/// <summary>Sets the maximum wait time. Call after strategy-specific configuration.</summary>
	public WaitStrategy WithTimeout(TimeSpan timeout)
	{
		Timeout = timeout;
		return this;
	}

	/// <summary>Sets the delay between readiness checks.</summary>
	public WaitStrategy WithInterval(TimeSpan interval)
	{
		Interval = interval;
		return this;
	}

	/// <summary>Sets the maximum number of failed checks before giving up.</summary>
	public WaitStrategy WithRetries(int retries)
	{
		Retries = retries;
		return this;
	}
}
