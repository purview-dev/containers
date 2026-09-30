using MySqlConnector;

namespace Purview.Containers.MySql;

/// <summary>A throwaway MySQL database running on WSL Containers.</summary>
public sealed class MySqlContainer : ContainerBase
{
	readonly MySqlConfiguration _configuration;

	internal MySqlContainer(MySqlConfiguration configuration, IContainerBackend? backend)
		: base(configuration, backend)
	{
		_configuration = configuration;
	}

	/// <summary>Connection string pointing at the mapped host port. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString()
	{
		MySqlConnectionStringBuilder builder = new()
		{
			Server = "127.0.0.1",
			Port = GetMappedPublicPort(MySqlBuilder.MySqlPort),
			Database = _configuration.Database,
			UserID = _configuration.Username,
			Password = _configuration.Password.Value,
		};
		return builder.ConnectionString;
	}
}
