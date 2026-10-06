using System.Globalization;
using Purview.Containers.Docker;
using TUnit.Core.Exceptions;

namespace Purview.Containers.Modules.Docker;

/// <summary>
/// Every service module on the Docker backend. The WSLC suites prove the modules against WSL Containers;
/// this suite proves the same module packages produce working containers on Docker, which is what makes a
/// developer machine (WSLC) and a Linux CI runner (Docker) interchangeable.
/// </summary>
/// <remarks>
/// Readiness is the module's own strategy, so a passing test means the service was really up: PostgreSQL
/// waits for <c>pg_isready</c>, Redis for <c>redis-cli ping</c>, SQL Server and MySQL for a real client
/// connection, and RabbitMQ, Azurite and NATS for their startup log lines. Each test skips itself when no
/// Docker daemon is reachable.
/// </remarks>
public class ModuleDockerTests
{
	static int BackendRegistered;

	static async Task SkipIfUnavailableAsync()
	{
		if (Interlocked.Exchange(ref BackendRegistered, 1) == 0)
		{
			ContainerBackends.Register(DockerContainerBackend.Create());
		}

		var info = await DockerContainerBackend.Create().GetInfoAsync();
		if (!info.IsUsable)
		{
			throw new SkipTestException($"Docker is unavailable: {string.Join("; ", info.MissingComponents)}");
		}
	}

	static async Task AssertStartedAsync(IContainer container, ushort port)
	{
		await container.StartAsync();

		await Assert.That(container.State).IsEqualTo(ContainerState.Running);
		await Assert.That(container.GetMappedPublicPort(port)).IsGreaterThan((ushort)0);
	}

	[Test]
	public async Task PostgreSql_StartsAndMapsItsPort()
	{
		await SkipIfUnavailableAsync();

		await using var postgres = new PostgreSql.PostgreSqlBuilder()
			.WithDatabase("docker_tests")
			.WithUsername("postgres")
			.WithPassword("postgres")
			.Build();

		await AssertStartedAsync(postgres, PostgreSql.PostgreSqlBuilder.PostgreSqlPort);
		await Assert.That(postgres.GetConnectionString()).Contains("docker_tests");
	}

	[Test]
	public async Task Redis_StartsAndMapsItsPort()
	{
		await SkipIfUnavailableAsync();

		await using var redis = new Redis.RedisBuilder().Build();

		await AssertStartedAsync(redis, Redis.RedisBuilder.RedisPort);

		// The connection string points at the random host port, not the container port.
		var hostPort = redis.GetMappedPublicPort(Redis.RedisBuilder.RedisPort);
		await Assert.That(redis.GetConnectionString()).Contains(hostPort.ToString(CultureInfo.InvariantCulture));
	}

	[Test]
	public async Task MsSql_StartsAndMapsItsPort()
	{
		await SkipIfUnavailableAsync();

		await using var sqlServer = new MsSql.MsSqlBuilder()
			.WithPassword("SomeStrong!Password1")
			.AcceptLicense()
			.Build();

		await AssertStartedAsync(sqlServer, MsSql.MsSqlBuilder.MsSqlPort);
		await Assert.That(sqlServer.GetConnectionString()).Contains("Password=SomeStrong!Password1");
	}

	[Test]
	public async Task MySql_StartsAndMapsItsPort()
	{
		await SkipIfUnavailableAsync();

		await using var mySql = new MySql.MySqlBuilder().WithPassword("SomeStrong!Password1").Build();

		await AssertStartedAsync(mySql, MySql.MySqlBuilder.MySqlPort);
		await Assert.That(mySql.GetConnectionString()).IsNotEmpty();
	}

	[Test]
	public async Task RabbitMq_StartsAndMapsItsPorts()
	{
		await SkipIfUnavailableAsync();

		await using var rabbitMq = new RabbitMq.RabbitMqBuilder().WithPassword("guest").Build();

		await AssertStartedAsync(rabbitMq, RabbitMq.RabbitMqBuilder.AmqpPort);
		await Assert.That(rabbitMq.GetAmqpEndpoint().Port).IsGreaterThan(0);
	}

	[Test]
	public async Task Azurite_StartsAndMapsItsPort()
	{
		await SkipIfUnavailableAsync();

		await using var azurite = new Azurite.AzuriteBuilder().Build();

		await AssertStartedAsync(azurite, Azurite.AzuriteBuilder.BlobPort);
		await Assert.That(azurite.GetBlobEndpoint().Port).IsGreaterThan(0);
	}

	[Test]
	public async Task Nats_StartsAndMapsItsPort()
	{
		await SkipIfUnavailableAsync();

		await using var nats = new Nats.NatsBuilder().Build();

		await AssertStartedAsync(nats, Nats.NatsBuilder.ClientPort);
		await Assert.That(nats.GetClientEndpoint().Port).IsGreaterThan(0);
	}

	[Test]
	public async Task AzureKeyVaultEmulator_StoresAndReturnsASecret()
	{
		await SkipIfUnavailableAsync();

		// Trust-store installation is disabled: the module's clients pin the emulator certificate, so the
		// runner's trust store is never modified.
		await using var emulator = new AzureKeyVaultEmulator.AzureKeyVaultEmulatorBuilder()
			.WithTrustStoreInstallation(false)
			.Build();

		await AssertStartedAsync(emulator, AzureKeyVaultEmulator.AzureKeyVaultEmulatorBuilder.EmulatorPort);

		var client = emulator.GetSecretClient();
		await client.SetSecretAsync("docker-integration-secret", "s3cret");

		var secret = await client.GetSecretAsync("docker-integration-secret");

		await Assert.That(secret.Value.Value).IsEqualTo("s3cret");
		await Assert.That(emulator.GetVaultUri().Scheme).IsEqualTo("https");
	}
}
