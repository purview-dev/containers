namespace Purview.WslContainers.Waiting;

/// <summary>A composable readiness check evaluated until the container is ready or the timeout expires.</summary>
public interface IWaitStrategy
{
	/// <summary>Maximum time to wait for readiness. When <c>null</c>, the container startup timeout is used.</summary>
	TimeSpan? Timeout { get; }

	/// <summary>Delay between readiness checks.</summary>
	TimeSpan Interval { get; }

	/// <summary>Optional maximum number of failed checks. When set, readiness fails after this many failures.</summary>
	int? Retries { get; }

	/// <summary>Evaluates readiness once. Returns <c>true</c> when the container is ready.</summary>
	Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken);
}
