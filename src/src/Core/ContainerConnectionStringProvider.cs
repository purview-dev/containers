namespace Purview.Containers;

/// <summary>
/// Base implementation for container connection string providers. Subclasses supply the host connection
/// string (and, when supported, a container-to-container one); this type wires configuration, mode dispatch
/// and the guard errors.
/// </summary>
public abstract class ContainerConnectionStringProvider<TContainer, TConfiguration>
	: IConnectionStringProvider<TContainer, TConfiguration>
	where TContainer : IContainer
	where TConfiguration : IContainerConfiguration
{
	/// <summary>The started container, available after <see cref="Configure" />.</summary>
	protected TContainer Container { get; private set; } = default!;

	/// <summary>The container configuration, available after <see cref="Configure" />.</summary>
	protected TConfiguration Configuration { get; private set; } = default!;

	/// <inheritdoc />
	public virtual void Configure(TContainer container, TConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(container);
		ArgumentNullException.ThrowIfNull(configuration);
		Container = container;
		Configuration = configuration;
	}

	/// <inheritdoc />
	public virtual string GetConnectionString(ConnectionMode connectionMode = ConnectionMode.Host)
	{
		EnsureConfigured();

		var connectionString = connectionMode switch
		{
			ConnectionMode.Host => GetHostConnectionString(),
			ConnectionMode.Container => GetContainerConnectionString(),
			_ => throw new ArgumentOutOfRangeException(nameof(connectionMode), connectionMode, null),
		};

		if (string.IsNullOrWhiteSpace(connectionString))
		{
			throw new ConnectionStringNotAvailableException(connectionMode, GetType());
		}

		return connectionString;
	}

	/// <inheritdoc />
	public virtual string GetConnectionString(string name, ConnectionMode connectionMode = ConnectionMode.Host)
	{
		ArgumentNullException.ThrowIfNull(name);
		EnsureConfigured();
		throw new ConnectionStringNameNotSupportedException(GetType(), name);
	}

	/// <summary>Builds the host connection string. Called for <see cref="ConnectionMode.Host" />.</summary>
	protected abstract string GetHostConnectionString();

	/// <summary>
	/// Builds the container-to-container connection string. The default throws
	/// <see cref="ConnectionStringModeNotSupportedException" /> because neither backend supports
	/// container-to-container networking yet.
	/// </summary>
	protected virtual string GetContainerConnectionString()
	{
		throw new ConnectionStringModeNotSupportedException(ConnectionMode.Container, GetType());
	}

	void EnsureConfigured()
	{
		if (Container is null || Configuration is null)
		{
			throw new ConnectionStringProviderNotConfiguredException();
		}
	}
}
