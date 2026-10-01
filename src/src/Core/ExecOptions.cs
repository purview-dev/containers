namespace Purview.Containers;

/// <summary>Options for <see cref="IContainer.ExecAsync" />.</summary>
public sealed record ExecOptions
{
	/// <summary>Environment variables set for the executed process.</summary>
	public IReadOnlyDictionary<string, string>? Environment { get; init; }

	/// <summary>Working directory for the executed process.</summary>
	public string? WorkingDirectory { get; init; }

	/// <summary>Maximum time to wait for the process to exit. When exceeded, the process is signalled.</summary>
	public TimeSpan? Timeout { get; init; }

	/// <summary>Enable standard input for the process. Not yet exposed via the synchronous capture path.</summary>
	public bool EnableStandardInput { get; init; }
}
