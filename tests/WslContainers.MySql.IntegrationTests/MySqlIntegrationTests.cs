using MySqlConnector;
using Purview.WslContainers.MySql;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.MySql.IntegrationTests;

public class MySqlIntegrationTests
{
	[Test]
	public async Task MySql_ConnectsAndRunsQuery()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using MySqlContainer mySql = new MySqlBuilder()
			.WithDatabase("integration")
			.WithUsername("test")
			.WithPassword("test")
			.Build();

		await mySql.StartAsync();

		await using MySqlConnection connection = new MySqlConnection(mySql.GetConnectionString());
		await connection.OpenAsync();
		await using MySqlCommand command = new MySqlCommand("SELECT 1", connection);
		object result = await command.ExecuteScalarAsync();

		await Assert.That(Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(1);
	}

	[Test]
	public async Task ConnectionString_UsesMappedRandomPort()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using MySqlContainer mySql = new MySqlBuilder().Build();

		await mySql.StartAsync();

		MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder(mySql.GetConnectionString());
		ushort port = mySql.GetMappedPublicPort(MySqlBuilder.MySqlPort);

		await Assert.That(builder.Server).IsEqualTo("127.0.0.1");
		await Assert.That(builder.Port).IsEqualTo(port);
		await Assert.That(builder.Database).IsEqualTo("test");
	}
}
