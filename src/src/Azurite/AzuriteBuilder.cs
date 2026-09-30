using Purview.WslContainers.Waiting;

namespace Purview.WslContainers.Azurite;

/// <summary>Fluent builder for an Azurite (Azure Storage emulator) test container.</summary>
public class AzuriteBuilder : ContainerBuilder<AzuriteBuilder, AzuriteContainer, AzuriteConfiguration>
{
	/// <summary>Default blob service port.</summary>
	public const ushort BlobPort = 10000;

	/// <summary>Default queue service port.</summary>
	public const ushort QueuePort = 10001;

	/// <summary>Default table service port.</summary>
	public const ushort TablePort = 10002;

	/// <summary>Default image.</summary>
	public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite";

	/// <summary>Creates a builder with the default image.</summary>
	public AzuriteBuilder()
		: this(AzuriteImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public AzuriteBuilder(string image)
	{
		WithImage(image)
			.WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0")
			.WithPortBinding(BlobPort, assignRandomHostPort: true)
			.WithPortBinding(QueuePort, assignRandomHostPort: true)
			.WithPortBinding(TablePort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public AzuriteBuilder(IContainerRuntime runtime)
		: base(runtime)
	{
		WithImage(AzuriteImage)
			.WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0")
			.WithPortBinding(BlobPort, assignRandomHostPort: true)
			.WithPortBinding(QueuePort, assignRandomHostPort: true)
			.WithPortBinding(TablePort, assignRandomHostPort: true);
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal AzuriteConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override AzuriteConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		var waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForLogMessage("successfully listening") };
		return configuration with { WaitStrategies = waitStrategies };
	}

	/// <inheritdoc />
	protected override AzuriteContainer CreateContainer(AzuriteConfiguration configuration) =>
		new(configuration, Runtime ?? WslContainerRuntime.Instance);
}
