namespace Purview.WslContainers.Containers;

/// <summary>Standard output stream of a container or exec process.</summary>
public enum LogOutput
{
	/// <summary>Standard output.</summary>
	Stdout = 0,

	/// <summary>Standard error.</summary>
	Stderr = 1,
}
