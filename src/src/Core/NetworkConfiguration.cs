namespace Purview.Containers;

/// <summary>Configuration for a managed bridge network with container-name/alias DNS.</summary>
public sealed record NetworkConfiguration
{
	/// <summary>Optional network name. A unique name is generated when omitted.</summary>
	public string? Name { get; init; }

	/// <summary>Maximum managed create/delete wait.</summary>
	public TimeSpan Timeout
	{
		get;
		init
		{
			if (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1)
				throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be positive and finite.");
			field = value;
		}
	} = TimeSpan.FromMinutes(1);
}
