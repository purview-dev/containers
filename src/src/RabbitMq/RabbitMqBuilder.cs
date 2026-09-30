using Purview.Containers.Diagnostics;
using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.RabbitMq;

/// <summary>Fluent builder for a RabbitMQ test container.</summary>
public class RabbitMqBuilder : ContainerBuilder<RabbitMqBuilder, RabbitMqContainer, RabbitMqConfiguration>
{
	/// <summary>Default AMQP port.</summary>
	public const ushort AmqpPort = 5672;

	/// <summary>Default management web port.</summary>
	public const ushort ManagementPort = 15672;

	/// <summary>Default image (includes the management plugin).</summary>
	public const string RabbitMqImage = "rabbitmq:3-management";

	string _username = "guest";
	Secret _password = Secret.From("guest");
	string _virtualHost = "/";

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
	public RabbitMqBuilder(IContainerBackend backend)
		: base(backend)
	{
		WithImage(RabbitMqImage)
			.WithPortBinding(AmqpPort, assignRandomHostPort: true)
			.WithPortBinding(ManagementPort, assignRandomHostPort: true);
	}

	/// <summary>Sets the AMQP username.</summary>
	public RabbitMqBuilder WithUsername(string username)
	{
		_username = username;
		WithEnvironment("RABBITMQ_DEFAULT_USER", username);
		return this;
	}

	/// <summary>Sets the AMQP password.</summary>
	public RabbitMqBuilder WithPassword(string password)
	{
		_password = Secret.From(password);
		WithEnvironment("RABBITMQ_DEFAULT_PASS", password);
		return this;
	}

	/// <summary>Sets the default virtual host.</summary>
	public RabbitMqBuilder WithVirtualHost(string virtualHost)
	{
		_virtualHost = virtualHost;
		WithEnvironment("RABBITMQ_DEFAULT_VHOST", virtualHost);
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal RabbitMqConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override RabbitMqConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new(configuration.Environment, StringComparer.Ordinal)
		{
			["RABBITMQ_DEFAULT_USER"] = _username,
			["RABBITMQ_DEFAULT_PASS"] = _password.Value,
			["RABBITMQ_DEFAULT_VHOST"] = _virtualHost,
		};
		var waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForLogMessage("Server startup complete") };
		return configuration with
		{
			Username = _username,
			Password = _password,
			VirtualHost = _virtualHost,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (string.IsNullOrWhiteSpace(_username))
		{
			throw new ContainerConfigurationException("RabbitMQ username cannot be empty.");
		}

		if (string.IsNullOrEmpty(_password.Value))
		{
			throw new ContainerConfigurationException("RabbitMQ password cannot be empty.");
		}

		if (string.IsNullOrEmpty(_virtualHost))
		{
			throw new ContainerConfigurationException("RabbitMQ virtual host cannot be empty.");
		}
	}

	/// <inheritdoc />
	protected override RabbitMqContainer CreateContainer(RabbitMqConfiguration configuration)
	{
		return new RabbitMqContainer(configuration, Backend);
	}
}
