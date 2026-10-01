namespace Purview.Containers.Waiting;

/// <summary>Readiness-check context: the container plus resolved runtime state (ports, IP).</summary>
/// <remarks>Creates the readiness context for a started container.</remarks>
public sealed class WaitContext(
	IContainer container,
	IReadOnlyDictionary<ushort, ushort> portMappings,
	string? networkIp
)
{
	/// <summary>The container being checked.</summary>
	public IContainer Container { get; } = container;

	/// <summary>The container bridge IP (Bridged networking only), if known.</summary>
	public string? NetworkIp { get; } = networkIp;

	/// <summary>Returns the host port mapped to <paramref name="containerPort" />, or <c>null</c> when not mapped.</summary>
	public int? GetHostPort(ushort containerPort)
	{
		return portMappings.TryGetValue(containerPort, out var hostPort) ? hostPort : null;
	}

	/// <summary>The first mapped host port, or <c>null</c> when no ports are mapped.</summary>
	public int? FirstHostPort => portMappings.Count > 0 ? portMappings.Values.First() : null;
}
