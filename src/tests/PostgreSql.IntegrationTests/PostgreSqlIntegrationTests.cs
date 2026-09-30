using Npgsql;
using PurviewContainerState = Purview.WslContainers.Containers.ContainerState;

namespace Purview.WslContainers.PostgreSql;

class PostgreSqlIntegrationTests
{
	[Test]
	public async Task PostgreSql_ConnectsAndRunsQuery()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var postgres = new PostgreSqlBuilder()
			.WithDatabase("integration")
			.WithUsername("postgres")
			.WithPassword("postgres")
			.Build();

		await postgres.StartAsync();

		await using NpgsqlConnection connection = new(postgres.GetConnectionString());
		await connection.OpenAsync();
		await using NpgsqlCommand command = new("SELECT 1", connection);
		var result = await command.ExecuteScalarAsync();

		await Assert.That(Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(1);
	}

	[Test]
	public async Task ConnectionString_UsesMappedRandomPort()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var postgres = new PostgreSqlBuilder()
			.WithDatabase("integration")
			.WithUsername("postgres")
			.WithPassword("postgres")
			.Build();

		await postgres.StartAsync();

		NpgsqlConnectionStringBuilder builder = [with(postgres.GetConnectionString())];

		await Assert.That(builder.Host).IsEqualTo("localhost");
		await Assert.That(builder.Database).IsEqualTo("integration");
		await Assert.That(builder.Port).IsNotEqualTo(5432);
		await Assert.That(builder.Port).IsGreaterThan(0);
		await Assert.That(builder.Password).IsEqualTo("postgres");
	}

	[Test]
	public async Task StartAsync_WaitsForPostgresReadiness()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var postgres = new PostgreSqlBuilder().Build();

		await postgres.StartAsync();

		await Assert.That(postgres.State).IsEqualTo(PurviewContainerState.Running);
	}
}
