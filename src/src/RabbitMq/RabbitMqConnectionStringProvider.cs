namespace Purview.Containers.RabbitMq;

/// <summary>Provides the RabbitMQ connection string.</summary>
sealed class RabbitMqConnectionStringProvider
	: ContainerConnectionStringProvider<RabbitMqContainer, RabbitMqConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
