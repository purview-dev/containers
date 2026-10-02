namespace Purview.Containers.MsSql;

/// <summary>Provides the SQL Server connection string.</summary>
sealed class MsSqlConnectionStringProvider : ContainerConnectionStringProvider<MsSqlContainer, MsSqlConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
