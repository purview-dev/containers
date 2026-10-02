using Purview.Containers.Waiting;

namespace Purview.Containers.Redis;

/// <summary>
/// Fluent builder for a Redis-compatible test container. Works with Redis and Redis-compatible images
/// such as Valkey or Garnet via <c>WithImage(...)</c>.
/// </summary>
public class RedisBuilder : ContainerBuilder<RedisBuilder, RedisContainer, RedisConfiguration>
{
	/// <summary>Default Redis port.</summary>
	public const ushort RedisPort = 6379;

	/// <summary>Default image.</summary>
	public const string RedisImage = "redis:7";

	/// <summary>Creates a builder with the default image.</summary>
	public RedisBuilder()
		: this(RedisImage) { }

	/// <summary>Creates a builder with a custom image (e.g. <c>valkey/valkey:7</c>).</summary>
	public RedisBuilder(string image)
	{
		WithImage(image)
			.WithPortBinding(RedisPort, assignRandomHostPort: true)
			.WithConnectionStringProvider(new RedisConnectionStringProvider());
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public RedisBuilder(IContainerBackend backend)
		: base(backend)
	{
		WithImage(RedisImage)
			.WithPortBinding(RedisPort, assignRandomHostPort: true)
			.WithConnectionStringProvider(new RedisConnectionStringProvider());
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal RedisConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override RedisConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		var waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForCommand("redis-cli", "ping") };
		return configuration with { WaitStrategies = waitStrategies };
	}

	/// <inheritdoc />
	protected override RedisContainer CreateContainer(RedisConfiguration configuration)
	{
		return new RedisContainer(configuration, Backend);
	}
}
