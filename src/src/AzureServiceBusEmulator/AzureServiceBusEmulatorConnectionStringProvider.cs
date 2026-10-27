namespace Purview.Containers.AzureServiceBusEmulator;

/// <summary>Provides the emulator's Service Bus connection string.</summary>
sealed class AzureServiceBusEmulatorConnectionStringProvider
	: ContainerConnectionStringProvider<AzureServiceBusEmulatorContainer, AzureServiceBusEmulatorConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
