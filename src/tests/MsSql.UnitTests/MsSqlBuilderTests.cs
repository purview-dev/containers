using Purview.Containers.Runtime;

namespace Purview.Containers.MsSql;

public class MsSqlBuilderTests
{
	[Test]
	public async Task Build_WithoutAcceptLicense_Throws()
	{
		var builder = new MsSqlBuilder().WithPassword("SomeStrong!Password1");

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithWeakPassword_Throws()
	{
		var builder = new MsSqlBuilder().WithPassword("short").AcceptLicense();

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new MsSqlBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("mcr.microsoft.com/mssql/server:2022-latest");
		await Assert.That(configuration.Password.Value).IsEqualTo("YourStrong!Passw0rd");
		await Assert.That(configuration.Database).IsEqualTo("master");
		await Assert.That(configuration.AcceptLicense).IsFalse();
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(1);
		await Assert.That(configuration.PortBindings[0].ContainerPort).IsEqualTo((ushort)1433);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsTrue();
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
	}

	[Test]
	public async Task WithDatabase_OverridesTheInitialCatalog()
	{
		var configuration = new MsSqlBuilder().WithDatabase("app").BuildConfigurationForTesting();

		await Assert.That(configuration.Database).IsEqualTo("app");
	}

	[Test]
	public async Task WithDatabase_WithABlankName_Throws()
	{
		MsSqlBuilder builder = new();

		await Assert.That(() => builder.WithDatabase("  ")).Throws<ArgumentException>();
	}

	[Test]
	public async Task BuildConfig_HonoursModuleSetters()
	{
		var configuration = new MsSqlBuilder()
			.WithPassword("SomeStrong!Password1")
			.AcceptLicense()
			.BuildConfigurationForTesting();

		await Assert.That(configuration.Password.Value).IsEqualTo("SomeStrong!Password1");
		await Assert.That(configuration.AcceptLicense).IsTrue();
		await Assert.That(configuration.Environment["MSSQL_SA_PASSWORD"]).IsEqualTo("SomeStrong!Password1");
		await Assert.That(configuration.Environment["ACCEPT_EULA"]).IsEqualTo("Y");
		await Assert.That(configuration.Environment["MSSQL_PID"]).IsEqualTo("Developer");
	}
}
