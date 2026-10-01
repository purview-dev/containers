namespace Purview.Containers.Wsl;

/// <summary>Information about the installed WSL Containers runtime.</summary>
public sealed record WslContainerRuntimeInfo(
	string Version,
	IReadOnlyList<string> MissingComponents,
	bool IsAvailable,
	bool IsCompatible
);
