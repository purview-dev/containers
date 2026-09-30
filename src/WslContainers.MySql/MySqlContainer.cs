using MySqlConnector;

namespace Purview.WslContainers.MySql;

/// <summary>A throwaway MySQL database running on WSL Containers.</summary>
public sealed class MySqlContainer : WslContainer
{
	private readonly MySqlConfiguration configuration;

	internal MySqlContainer(MySqlConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
	{
		this.configuration = configuration;
	}

	/// <summary>Connection string pointing at the mapped host port. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString()
	{
		MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
		{
			Server = "127.0.0.1",
			Port = GetMappedPublicPort(MySqlBuilder.MySqlPort),
			Database = configuration.Database,
			UserID = configuration.Username,
			Password = configuration.Password.Value,
		};
		return builder.ConnectionString;
	}
}
