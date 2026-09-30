using Npgsql;
using Purview.WslContainers;
using Purview.WslContainers.PostgreSql;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.PostgreSql.IntegrationTests;

public class PostgreSqlIntegrationTests
{
	[Test]
	public async Task PostgreSql_ConnectsAndRunsQuery()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using PostgreSqlContainer postgres = new PostgreSqlBuilder()
			.WithDatabase("integration")
			.WithUsername("postgres")
			.WithPassword("postgres")
			.Build();

		await postgres.StartAsync();

		await using NpgsqlConnection connection = new NpgsqlConnection(postgres.GetConnectionString());
		await connection.OpenAsync();
		await using NpgsqlCommand command = new NpgsqlCommand("SELECT 1", connection);
		object result = await command.ExecuteScalarAsync();

		await Assert.That(Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(1);
	}

	[Test]
	public async Task ConnectionString_UsesMappedRandomPort()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using PostgreSqlContainer postgres = new PostgreSqlBuilder()
			.WithDatabase("integration")
			.WithUsername("postgres")
			.WithPassword("postgres")
			.Build();

		await postgres.StartAsync();

		NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());

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

		await using PostgreSqlContainer postgres = new PostgreSqlBuilder().Build();

		await postgres.StartAsync();

		await Assert.That(postgres.State).IsEqualTo(ContainerState.Running);
	}
}
