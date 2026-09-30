namespace Purview.WslContainers.RabbitMq;

/// <summary>Fluent builder for a RabbitMQ test container.</summary>
public class RabbitMqBuilder : ContainerBuilder<RabbitMqBuilder, RabbitMqContainer, RabbitMqConfiguration>
{
	/// <summary>Default AMQP port.</summary>
	public const ushort AmqpPort = 5672;

	/// <summary>Default management web port.</summary>
	public const ushort ManagementPort = 15672;

	/// <summary>Default image (includes the management plugin).</summary>
	public const string RabbitMqImage = "rabbitmq:3-management";

	private string username = "guest";
	private Secret password = Secret.From("guest");
	private string virtualHost = "/";

	/// <summary>Creates a builder with the default image.</summary>
	public RabbitMqBuilder()
		: this(RabbitMqImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public RabbitMqBuilder(string image)
	{
		WithImage(image)
			.WithPortBinding(AmqpPort, assignRandomHostPort: true)
			.WithPortBinding(ManagementPort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public RabbitMqBuilder(IContainerRuntime runtime)
		: base(runtime)
	{
		WithImage(RabbitMqImage)
			.WithPortBinding(AmqpPort, assignRandomHostPort: true)
			.WithPortBinding(ManagementPort, assignRandomHostPort: true);
	}

	/// <summary>Sets the AMQP username.</summary>
	public RabbitMqBuilder WithUsername(string username)
	{
		this.username = username;
		WithEnvironment("RABBITMQ_DEFAULT_USER", username);
		return this;
	}

	/// <summary>Sets the AMQP password.</summary>
	public RabbitMqBuilder WithPassword(string password)
	{
		this.password = Secret.From(password);
		WithEnvironment("RABBITMQ_DEFAULT_PASS", password);
		return this;
	}

	/// <summary>Sets the default virtual host.</summary>
	public RabbitMqBuilder WithVirtualHost(string virtualHost)
	{
		this.virtualHost = virtualHost;
		WithEnvironment("RABBITMQ_DEFAULT_VHOST", virtualHost);
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal RabbitMqConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override RabbitMqConfiguration BuildConfiguration()
	{
		RabbitMqConfiguration configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new Dictionary<string, string>(
			configuration.Environment,
			StringComparer.Ordinal
		)
		{
			["RABBITMQ_DEFAULT_USER"] = username,
			["RABBITMQ_DEFAULT_PASS"] = password.Value,
			["RABBITMQ_DEFAULT_VHOST"] = virtualHost,
		};
		IReadOnlyList<IWaitStrategy> waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForLogMessage("Server startup complete") };
		return configuration with
		{
			Username = username,
			Password = password,
			VirtualHost = virtualHost,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (string.IsNullOrWhiteSpace(username))
		{
			throw new WslContainerConfigurationException("RabbitMQ username cannot be empty.");
		}

		if (string.IsNullOrEmpty(password.Value))
		{
			throw new WslContainerConfigurationException("RabbitMQ password cannot be empty.");
		}

		if (string.IsNullOrEmpty(virtualHost))
		{
			throw new WslContainerConfigurationException("RabbitMQ virtual host cannot be empty.");
		}
	}

	/// <inheritdoc />
	protected override RabbitMqContainer CreateContainer(RabbitMqConfiguration configuration)
	{
		return new RabbitMqContainer(configuration, Runtime ?? WslContainerRuntime.Instance);
	}
}
