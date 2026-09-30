namespace Purview.WslContainers.Redis;

/// <summary>A throwaway Redis-compatible instance running on WSL Containers.</summary>
public sealed class RedisContainer : WslContainer
{
	internal RedisContainer(RedisConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime) { }

	/// <summary>Connection string pointing at the mapped host port. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString()
	{
		return $"localhost:{GetMappedPublicPort(RedisBuilder.RedisPort)}";
	}
}
