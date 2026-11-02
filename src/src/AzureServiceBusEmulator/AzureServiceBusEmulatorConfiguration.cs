using Purview.Containers.Diagnostics;

namespace Purview.Containers.AzureServiceBusEmulator;

/// <summary>Immutable configuration for an Azure Service Bus Emulator test container.</summary>
public sealed record AzureServiceBusEmulatorConfiguration : ContainerConfiguration
{
	/// <summary>True when the emulator and SQL Server EULAs have been explicitly accepted.</summary>
	public bool AcceptLicense { get; init; }

	/// <summary>Image of the SQL Server sidecar the emulator stores its state in.</summary>
	public string SqlServerImage { get; init; } = AzureServiceBusEmulatorBuilder.DefaultSqlServerImage;

	/// <summary>SA password of the SQL Server sidecar. Rendered as redacted.</summary>
	public Secret SqlServerPassword { get; init; } =
		Secret.From(AzureServiceBusEmulatorBuilder.DefaultSqlServerPassword);

	/// <summary>
	/// The emulator <c>Config.json</c> (namespaces, queues, topics, subscriptions), or <c>null</c> to use the
	/// configuration baked into the image.
	/// </summary>
	public string? ConfigJson { get; init; }
}
