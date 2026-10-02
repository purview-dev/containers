namespace Purview.Containers;

/// <summary>
/// Produces connection strings (or endpoints) for a container, uniformly across modules. Surfaced through
/// <see cref="IContainer" />.
/// </summary>
public interface IConnectionStringProvider
{
	/// <summary>Returns the connection string for the given connection mode.</summary>
	string GetConnectionString(ConnectionMode connectionMode = ConnectionMode.Host);

	/// <summary>Returns a named connection string (for modules with multiple endpoints, e.g. Azurite blob/queue/table).</summary>
	string GetConnectionString(string name, ConnectionMode connectionMode = ConnectionMode.Host);
}

/// <summary>
/// A connection string provider bound to a specific container and configuration. <see cref="Configure" />
/// is invoked once, after the container has started, so runtime-assigned ports are available.
/// </summary>
public interface IConnectionStringProvider<TContainer, TConfiguration> : IConnectionStringProvider
	where TContainer : IContainer
	where TConfiguration : IContainerConfiguration
{
	/// <summary>Initializes the provider with the started container and its configuration.</summary>
	void Configure(TContainer container, TConfiguration configuration);
}
