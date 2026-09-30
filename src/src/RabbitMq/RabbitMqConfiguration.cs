using Purview.Containers.Diagnostics;

namespace Purview.Containers.RabbitMq;

/// <summary>Immutable configuration for a RabbitMQ test container.</summary>
public sealed record RabbitMqConfiguration : ContainerConfiguration
{
	/// <summary>AMQP username (RABBITMQ_DEFAULT_USER).</summary>
	public string Username { get; init; } = "guest";

	/// <summary>AMQP password (RABBITMQ_DEFAULT_PASS). Rendered as redacted.</summary>
	public Secret Password { get; init; } = Secret.From("guest");

	/// <summary>Default virtual host (RABBITMQ_DEFAULT_VHOST).</summary>
	public string VirtualHost { get; init; } = "/";
}
