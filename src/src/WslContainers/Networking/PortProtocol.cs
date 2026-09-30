namespace Purview.WslContainers.Networking;

/// <summary>Port transport protocol.</summary>
public enum PortProtocol
{
	/// <summary>TCP.</summary>
	Tcp = 0,

	/// <summary>UDP. Not supported by WSLC (returns E_NOTIMPL).</summary>
	Udp = 1,
}
