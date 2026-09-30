namespace Purview.WslContainers;

/// <summary>Container networking mode. WSLC only supports none and bridged.</summary>
public enum ContainerNetworkingMode
{
	/// <summary>No network. The container has no IP address and cannot map ports.</summary>
	None = 0,

	/// <summary>Bridged networking. Required for host port mappings; the container receives a 172.17.x.x address.</summary>
	Bridged = 1,
}
