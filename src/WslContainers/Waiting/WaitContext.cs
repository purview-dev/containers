namespace Purview.WslContainers;

/// <summary>Readiness-check context: the container plus resolved runtime state (ports, IP).</summary>
public sealed class WaitContext
{
	private readonly IReadOnlyDictionary<ushort, ushort> portMappings;

	internal WaitContext(IContainer container, IReadOnlyDictionary<ushort, ushort> portMappings, string? networkIp)
	{
		Container = container;
		this.portMappings = portMappings;
		NetworkIp = networkIp;
	}

	/// <summary>The container being checked.</summary>
	public IContainer Container { get; }

	/// <summary>The container bridge IP (Bridged networking only), if known.</summary>
	public string? NetworkIp { get; }

	/// <summary>Returns the host port mapped to <paramref name="containerPort" />, or <c>null</c> when not mapped.</summary>
	public int? GetHostPort(ushort containerPort)
	{
		return portMappings.TryGetValue(containerPort, out ushort hostPort) ? hostPort : null;
	}

	/// <summary>The first mapped host port, or <c>null</c> when no ports are mapped.</summary>
	public int? FirstHostPort => portMappings.Count > 0 ? portMappings.Values.First() : null;
}
