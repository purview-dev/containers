using Microsoft.Data.SqlClient;
using Purview.WslContainers.MsSql;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.MsSql.IntegrationTests;

public class MsSqlIntegrationTests
{
	[Test]
	public async Task MsSql_ConnectsAndRunsQuery()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using MsSqlContainer sqlServer = new MsSqlBuilder()
			.WithPassword("SomeStrong!Password1")
			.AcceptLicense()
			.Build();

		await sqlServer.StartAsync();

		await using SqlConnection connection = new SqlConnection(sqlServer.GetConnectionString());
		await connection.OpenAsync();
		await using SqlCommand command = new SqlCommand("SELECT 1", connection);
		object result = await command.ExecuteScalarAsync();

		await Assert.That(Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(1);
	}

	[Test]
	public async Task ConnectionString_UsesMappedRandomPort()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using MsSqlContainer sqlServer = new MsSqlBuilder()
			.WithPassword("SomeStrong!Password1")
			.AcceptLicense()
			.Build();

		await sqlServer.StartAsync();

		SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(sqlServer.GetConnectionString());
		ushort port = sqlServer.GetMappedPublicPort(MsSqlBuilder.MsSqlPort);

		await Assert.That(builder.DataSource).IsEqualTo($"127.0.0.1,{port}");
		await Assert.That(port).IsGreaterThan((ushort)0);
		await Assert.That(builder.UserID).IsEqualTo("sa");
	}
}
