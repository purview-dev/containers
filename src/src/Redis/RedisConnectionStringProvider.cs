namespace Purview.Containers.Redis;

/// <summary>Provides the Redis connection string.</summary>
sealed class RedisConnectionStringProvider : ContainerConnectionStringProvider<RedisContainer, RedisConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
