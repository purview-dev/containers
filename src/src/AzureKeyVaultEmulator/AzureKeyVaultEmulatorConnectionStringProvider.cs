namespace Purview.Containers.AzureKeyVaultEmulator;

/// <summary>Provides the emulator's vault URI as the connection string.</summary>
sealed class AzureKeyVaultEmulatorConnectionStringProvider
	: ContainerConnectionStringProvider<AzureKeyVaultEmulatorContainer, AzureKeyVaultEmulatorConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
