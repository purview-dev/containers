namespace Purview.WslContainers.Waiting;

/// <summary>Readiness-check context: the container plus resolved runtime state (ports, IP).</summary>
public sealed class WaitContext
{
	readonly IReadOnlyDictionary<ushort, ushort> _portMappings;

	internal WaitContext(IContainer container, IReadOnlyDictionary<ushort, ushort> portMappings, string? networkIp)
	{
		Container = container;
		_portMappings = portMappings;
		NetworkIp = networkIp;
	}

	/// <summary>The container being checked.</summary>
	public IContainer Container { get; }

	/// <summary>The container bridge IP (Bridged networking only), if known.</summary>
	public string? NetworkIp { get; }

	/// <summary>Returns the host port mapped to <paramref name="containerPort" />, or <c>null</c> when not mapped.</summary>
	public int? GetHostPort(ushort containerPort)
	{
		return _portMappings.TryGetValue(containerPort, out var hostPort) ? hostPort : null;
	}

	/// <summary>The first mapped host port, or <c>null</c> when no ports are mapped.</summary>
	public int? FirstHostPort => _portMappings.Count > 0 ? _portMappings.Values.First() : null;
}
