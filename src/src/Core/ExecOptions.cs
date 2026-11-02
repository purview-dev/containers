namespace Purview.Containers;

/// <summary>Options for <see cref="IContainer.ExecAsync" />.</summary>
public sealed record ExecOptions
{
	/// <summary>Environment variables set for the executed process.</summary>
	public IReadOnlyDictionary<string, string>? Environment { get; init; }

	/// <summary>Working directory for the executed process.</summary>
	public string? WorkingDirectory { get; init; }

	/// <summary>Maximum managed exec wait. WSLC signals the process on cancellation; Docker may leave it running.</summary>
	public TimeSpan? Timeout
	{
		get;
		init
		{
			if (value is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
				throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be positive and finite.");
			field = value;
		}
	}

	/// <summary>Enable standard input for the process. Not yet exposed via the synchronous capture path.</summary>
	public bool EnableStandardInput { get; init; }
}
