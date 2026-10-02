namespace Purview.Containers;

#pragma warning disable CA1032 // Implement standard exception constructors

/// <summary>Thrown when a connection string provider has not been configured before use.</summary>
public sealed class ConnectionStringProviderNotConfiguredException : Exception
{
	public ConnectionStringProviderNotConfiguredException()
		: base("No connection string provider is configured for this container.") { }
}

/// <summary>Thrown when a connection string provider cannot produce a connection string for a mode.</summary>
public sealed class ConnectionStringNotAvailableException(ConnectionMode connectionMode, Type providerType)
	: InvalidOperationException(
		$"The connection string provider '{providerType.FullName}' did not return a connection string for connection mode '{connectionMode}'."
	) { }

/// <summary>Thrown when a provider does not support a requested connection mode.</summary>
public sealed class ConnectionStringModeNotSupportedException(ConnectionMode connectionMode, Type providerType)
	: InvalidOperationException(
		$"The connection string provider '{providerType.FullName}' does not support connection mode '{connectionMode}'."
	) { }

/// <summary>Thrown when a provider does not support a requested named connection string.</summary>
public sealed class ConnectionStringNameNotSupportedException(Type providerType, string name)
	: InvalidOperationException(
		$"The connection string provider '{providerType.FullName}' does not support the named connection string '{name}'."
	) { }
