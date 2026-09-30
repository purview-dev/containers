using Purview.WslContainers.Redis;
using StackExchange.Redis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.Redis.IntegrationTests;

public class RedisIntegrationTests
{
	[Test]
	public async Task Redis_RespondsToPing()
	{
		await Purview.WslContainers.Testing.WslcTest.SkipIfUnavailableAsync();

		await using RedisContainer redis = new RedisBuilder().Build();

		await redis.StartAsync();

		await using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(
			redis.GetConnectionString()
		);
		IDatabase db = connection.GetDatabase();
		RedisResult pong = await db.ExecuteAsync("PING");

		await Assert.That(pong.ToString()).IsEqualTo("PONG");
	}

	[Test]
	public async Task ConnectionString_UsesMappedRandomPort()
	{
		await Purview.WslContainers.Testing.WslcTest.SkipIfUnavailableAsync();

		await using RedisContainer redis = new RedisBuilder().Build();

		await redis.StartAsync();

		string connectionString = redis.GetConnectionString();
		ushort port = redis.GetMappedPublicPort(RedisBuilder.RedisPort);

		await Assert.That(connectionString).IsEqualTo($"localhost:{port}");
		await Assert.That(port).IsNotEqualTo(RedisBuilder.RedisPort);
	}
}
