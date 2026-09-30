using Npgsql;

namespace Purview.WslContainers.PostgreSql;

/// <summary>A throwaway PostgreSQL database running on WSL Containers.</summary>
public sealed class PostgreSqlContainer : WslContainer
{
	private readonly PostgreSqlConfiguration configuration;

	internal PostgreSqlContainer(PostgreSqlConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
	{
		this.configuration = configuration;
	}

	/// <summary>Connection string pointing at the mapped host port. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString()
	{
		NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
		{
			Host = "localhost",
			Port = GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort),
			Database = configuration.Database,
			Username = configuration.Username,
			Password = configuration.Password.Value,
		};
		return builder.ConnectionString;
	}
}
