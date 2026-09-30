namespace Purview.WslContainers;

/// <summary>Result of executing a command inside a container.</summary>
public sealed record ExecResult(long ExitCode, string Stdout, string Stderr)
{
	/// <summary>True when the process exited with code 0.</summary>
	public bool IsSuccess => ExitCode == 0;
}
