namespace Purview.Containers.MySql;

/// <summary>Provides the MySQL connection string.</summary>
sealed class MySqlConnectionStringProvider : ContainerConnectionStringProvider<MySqlContainer, MySqlConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
