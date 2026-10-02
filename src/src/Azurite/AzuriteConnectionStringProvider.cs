namespace Purview.Containers.Azurite;

/// <summary>Provides the Azurite connection string.</summary>
sealed class AzuriteConnectionStringProvider : ContainerConnectionStringProvider<AzuriteContainer, AzuriteConfiguration>
{
	/// <inheritdoc />
	protected override string GetHostConnectionString() => Container.GetConnectionString();
}
