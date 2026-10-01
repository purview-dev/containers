using Purview.Containers.Runtime;

namespace Purview.Containers.MySql;

public class MySqlBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new MySqlBuilder().BuildConfigurationForTesting();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/mysql:8");
		await Assert.That(configuration.Database).IsEqualTo("test");
		await Assert.That(configuration.Username).IsEqualTo("test");
		await Assert.That(configuration.Password.Value).IsEqualTo("test");
		await Assert.That(configuration.Environment["MYSQL_DATABASE"]).IsEqualTo("test");
		await Assert.That(configuration.Environment["MYSQL_USER"]).IsEqualTo("test");
		await Assert.That(configuration.Environment["MYSQL_PASSWORD"]).IsEqualTo("test");
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(1);
		await Assert.That(configuration.PortBindings[0].ContainerPort).IsEqualTo((ushort)3306);
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
	}

	[Test]
	public async Task BuildConfig_EmptyPassword_Throws()
	{
		var builder = new MySqlBuilder().WithPassword(string.Empty);

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}
}
