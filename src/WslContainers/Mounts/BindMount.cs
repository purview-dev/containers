namespace Purview.WslContainers;

/// <summary>A bind mount of a Windows directory into the container.</summary>
public sealed record BindMount(string HostPath, string ContainerPath, bool ReadOnly = false);
