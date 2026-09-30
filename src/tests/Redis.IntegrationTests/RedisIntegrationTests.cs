using StackExchange.Redis;

namespace Purview.WslContainers.Redis;

public class RedisIntegrationTests
{
	[Test]
	public async Task Redis_RespondsToPing()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var redis = new RedisBuilder().Build();

		await redis.StartAsync();

		await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
		var db = connection.GetDatabase();
		var pong = await db.ExecuteAsync("PING");

		await Assert.That(pong.ToString()).IsEqualTo("PONG");
	}

	[Test]
	public async Task ConnectionString_UsesMappedRandomPort()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var redis = new RedisBuilder().Build();

		await redis.StartAsync();

		var connectionString = redis.GetConnectionString();
		var port = redis.GetMappedPublicPort(RedisBuilder.RedisPort);

		await Assert.That(connectionString).IsEqualTo($"localhost:{port}");
		await Assert.That(port).IsNotEqualTo(RedisBuilder.RedisPort);
	}
}
