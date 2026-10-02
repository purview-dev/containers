namespace Purview.Containers;

/// <summary>How a connection string targets the container: from the test host or from another container.</summary>
public enum ConnectionMode
{
	/// <summary>Test host to container (the mapped host port).</summary>
	Host = 0,

	/// <summary>Container to container (the container network).</summary>
	Container = 1,
}
