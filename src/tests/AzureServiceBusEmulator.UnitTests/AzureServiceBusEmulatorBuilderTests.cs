using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.AzureServiceBusEmulator;

public class AzureServiceBusEmulatorBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new AzureServiceBusEmulatorBuilder().AcceptLicense().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo(AzureServiceBusEmulatorBuilder.EmulatorImage);
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(2);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 5672 && p.AssignRandomHostPort);
		await Assert.That(configuration.PortBindings).Contains(p => p.ContainerPort == 5300 && p.AssignRandomHostPort);
		await Assert.That(configuration.Environment["ACCEPT_EULA"]).IsEqualTo("Y");
		await Assert.That(configuration.Environment["SQL_WAIT_INTERVAL"]).IsEqualTo("0");
		await Assert
			.That(configuration.Environment["MSSQL_SA_PASSWORD"])
			.IsEqualTo(AzureServiceBusEmulatorBuilder.DefaultSqlServerPassword);
		await Assert.That(configuration.SqlServerImage).IsEqualTo(AzureServiceBusEmulatorBuilder.DefaultSqlServerImage);
		await Assert.That(configuration.ConfigJson).IsNull();
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<HttpWaitStrategy>();
	}

	[Test]
	public async Task Build_WithoutLicenseAcceptance_Throws()
	{
		AzureServiceBusEmulatorBuilder builder = new();

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithAShortSqlServerPassword_Throws()
	{
		var builder = new AzureServiceBusEmulatorBuilder().AcceptLicense().WithSqlServerPassword("short");

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task BuildConfig_SqlServerSettings_AreHonoured()
	{
		var configuration = new AzureServiceBusEmulatorBuilder()
			.AcceptLicense()
			.WithSqlServerImage("mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04")
			.WithSqlServerPassword("Another!Passw0rd")
			.BuildConfigurationForTesting();

		await Assert
			.That(configuration.SqlServerImage)
			.IsEqualTo("mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04");
		await Assert.That(configuration.SqlServerPassword.Value).IsEqualTo("Another!Passw0rd");
		await Assert.That(configuration.Environment["MSSQL_SA_PASSWORD"]).IsEqualTo("Another!Passw0rd");
	}

	[Test]
	public async Task BuildConfig_Config_IsCarried()
	{
		const string json = """{ "UserConfig": { "Namespaces": [] } }""";

		var configuration = new AzureServiceBusEmulatorBuilder()
			.AcceptLicense()
			.WithConfig(json)
			.BuildConfigurationForTesting();

		await Assert.That(configuration.ConfigJson).IsEqualTo(json);
	}

	[Test]
	public async Task WithConfigFile_MissingFile_Throws()
	{
		AzureServiceBusEmulatorBuilder builder = new();

		await Assert
			.That(() => builder.WithConfigFile(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")))
			.Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task ToString_RedactsTheSqlServerPassword()
	{
		var configuration = new AzureServiceBusEmulatorBuilder()
			.AcceptLicense()
			.WithSqlServerPassword("Another!Passw0rd")
			.BuildConfigurationForTesting();

		await Assert.That(configuration.ToString()).DoesNotContain("Another!Passw0rd");
	}

	[Test]
	public async Task BuildConfig_PreservesCustomWaitStrategy()
	{
		var configuration = new AzureServiceBusEmulatorBuilder()
			.AcceptLicense()
			.WithWaitStrategy(Wait.ForTcpPort(5672))
			.BuildConfigurationForTesting();

		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<TcpPortWaitStrategy>();
	}
}
