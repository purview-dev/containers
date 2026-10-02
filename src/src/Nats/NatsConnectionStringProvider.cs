namespace Purview.Containers.Nats;

/// <summary>Provides the NATS connection string.</summary>
sealed class NatsConnectionStringProvider : ContainerConnectionStringProvider<NatsContainer, NatsConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
