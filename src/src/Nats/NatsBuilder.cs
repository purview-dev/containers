using Purview.Containers.Waiting;

namespace Purview.Containers.Nats;

/// <summary>Fluent builder for a NATS test container.</summary>
public class NatsBuilder : ContainerBuilder<NatsBuilder, NatsContainer, NatsConfiguration>
{
	/// <summary>Default client port.</summary>
	public const ushort ClientPort = 4222;

	/// <summary>Default monitoring (HTTP) port.</summary>
	public const ushort MonitoringPort = 8222;

	/// <summary>Default image.</summary>
	public const string NatsImage = "nats:2";

	/// <summary>Creates a builder with the default image.</summary>
	public NatsBuilder()
		: this(NatsImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public NatsBuilder(string image)
	{
		WithImage(image)
			.WithPortBinding(ClientPort, assignRandomHostPort: true)
			.WithPortBinding(MonitoringPort, assignRandomHostPort: true)
			.WithConnectionStringProvider(new NatsConnectionStringProvider());
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public NatsBuilder(IContainerBackend backend)
		: base(backend)
	{
		WithImage(NatsImage)
			.WithPortBinding(ClientPort, assignRandomHostPort: true)
			.WithPortBinding(MonitoringPort, assignRandomHostPort: true)
			.WithConnectionStringProvider(new NatsConnectionStringProvider());
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal NatsConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override NatsConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		var waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForLogMessage("Listening for client connections") };
		return configuration with { WaitStrategies = waitStrategies };
	}

	/// <inheritdoc />
	protected override NatsContainer CreateContainer(NatsConfiguration configuration)
	{
		return new NatsContainer(configuration, Backend);
	}
}
