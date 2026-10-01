namespace Purview.Containers.Networking;

/// <summary>A host-to-container port mapping.</summary>
/// <param name="ContainerPort">The port inside the container.</param>
/// <param name="HostPort">The host port to bind. When <c>null</c>, a random free host port is allocated natively by WSLC.</param>
/// <param name="Protocol">Transport protocol (TCP only; UDP is not supported by WSLC).</param>
/// <param name="HostAddress">Optional host address to bind (e.g. <c>127.0.0.1</c>). Defaults to IPv4 loopback.</param>
public sealed record PortBinding(
	ushort ContainerPort,
	ushort? HostPort = null,
	PortProtocol Protocol = PortProtocol.Tcp,
	string? HostAddress = null
)
{
	/// <summary>True when a random host port should be allocated.</summary>
	public bool AssignRandomHostPort => HostPort is null;
}
