namespace Purview.Containers.PostgreSql;

/// <summary>Provides the PostgreSQL connection string.</summary>
sealed class PostgreSqlConnectionStringProvider
	: ContainerConnectionStringProvider<PostgreSqlContainer, PostgreSqlConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
