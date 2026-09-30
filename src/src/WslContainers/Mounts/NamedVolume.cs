namespace Purview.WslContainers.Mounts;

/// <summary>A named volume (session VHD) mounted into the container. Auto-provisioned on first use.</summary>
public sealed record NamedVolume(string Name, string ContainerPath, bool ReadOnly = false);
