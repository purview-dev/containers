namespace Purview.Containers;

/// <summary>A Dockerfile build on the selected backend, not on the host's default engine.</summary>
public sealed record ImageBuildConfiguration
{
	/// <summary>The host directory containing the build context.</summary>
	public required string ContextDirectory { get; init; }

	/// <summary>The resulting image reference.</summary>
	public required string Name { get; init; }

	/// <summary>Dockerfile path relative to the build context.</summary>
	public string Dockerfile { get; init; } = "Dockerfile";

	/// <summary>Optional target stage.</summary>
	public string? Target { get; init; }

	/// <summary>Maximum managed build wait, including context transfer.</summary>
	public TimeSpan Timeout
	{
		get;
		init
		{
			if (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1)
				throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be positive and finite.");
			field = value;
		}
	} = TimeSpan.FromMinutes(5);
}
