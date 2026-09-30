using Purview.WslContainers;
using Purview.WslContainers.PostgreSql;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.PostgreSql.UnitTests;

public class PostgreSqlBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		PostgreSqlConfiguration configuration = new PostgreSqlBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/postgres:17");
		await Assert.That(configuration.Database).IsEqualTo("postgres");
		await Assert.That(configuration.Username).IsEqualTo("postgres");
		await Assert.That(configuration.Password.Value).IsEqualTo("postgres");
		await Assert.That(configuration.Environment["POSTGRES_DB"]).IsEqualTo("postgres");
		await Assert.That(configuration.Environment["POSTGRES_USER"]).IsEqualTo("postgres");
		await Assert.That(configuration.Environment["POSTGRES_PASSWORD"]).IsEqualTo("postgres");
		await Assert.That(configuration.PortBindings).HasCount().EqualTo(1);
		await Assert.That(configuration.PortBindings[0].ContainerPort).IsEqualTo((ushort)5432);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsTrue();
		await Assert.That(configuration.WaitStrategies).HasCount().EqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<CommandWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_HonoursModuleSetters()
	{
		PostgreSqlConfiguration configuration = new PostgreSqlBuilder()
			.WithDatabase("integration")
			.WithUsername("app")
			.WithPassword("s3cret")
			.BuildConfigurationForTesting();

		await Assert.That(configuration.Database).IsEqualTo("integration");
		await Assert.That(configuration.Username).IsEqualTo("app");
		await Assert.That(configuration.Password.Value).IsEqualTo("s3cret");
		await Assert.That(configuration.Environment["POSTGRES_DB"]).IsEqualTo("integration");
		await Assert.That(configuration.Environment["POSTGRES_USER"]).IsEqualTo("app");
		await Assert.That(configuration.Environment["POSTGRES_PASSWORD"]).IsEqualTo("s3cret");
	}

	[Test]
	public async Task BuildConfig_EmptyPassword_Throws()
	{
		PostgreSqlBuilder builder = new PostgreSqlBuilder().WithPassword(string.Empty);

		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task ConfigurationToString_RedactsPasswordAndEnvironment()
	{
		PostgreSqlConfiguration configuration = new PostgreSqlConfiguration
		{
			Image = "postgres:17",
			Environment = new Dictionary<string, string>
			{
				["POSTGRES_PASSWORD"] = "s3cret-password",
				["POSTGRES_USER"] = "postgres",
			},
			Password = Secret.From("s3cret-password"),
		};

		string rendered = configuration.ToString();

		await Assert.That(rendered).DoesNotContain("s3cret-password");
		await Assert.That(rendered).Contains("postgres");
	}
}
