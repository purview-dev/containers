namespace Purview.Containers.Redis;

/// <summary>A throwaway Redis-compatible instance running on WSL Containers.</summary>
public sealed class RedisContainer : ContainerBase
{
	internal RedisContainer(RedisConfiguration configuration, IContainerBackend? backend)
		: base(configuration, backend) { }

	/// <summary>Connection string pointing at the mapped host port. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString()
	{
		return $"localhost:{GetMappedPublicPort(RedisBuilder.RedisPort)}";
	}
}
