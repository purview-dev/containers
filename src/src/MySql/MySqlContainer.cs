using MySqlConnector;

namespace Purview.WslContainers.MySql;

/// <summary>A throwaway MySQL database running on WSL Containers.</summary>
public sealed class MySqlContainer : WslContainer
{
	readonly MySqlConfiguration _configuration;

	internal MySqlContainer(MySqlConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
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
