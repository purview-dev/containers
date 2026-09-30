using Npgsql;

namespace Purview.WslContainers.PostgreSql;

/// <summary>A throwaway PostgreSQL database running on WSL Containers.</summary>
public sealed class PostgreSqlContainer : WslContainer
{
	readonly PostgreSqlConfiguration _configuration;

	internal PostgreSqlContainer(PostgreSqlConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
	{
		_configuration = configuration;
	}

	/// <summary>Connection string pointing at the mapped host port. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString()
	{
		NpgsqlConnectionStringBuilder builder = new()
		{
			Host = "localhost",
			Port = GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort),
			Database = _configuration.Database,
			Username = _configuration.Username,
			Password = _configuration.Password.Value,
		};
		return builder.ConnectionString;
	}
}
