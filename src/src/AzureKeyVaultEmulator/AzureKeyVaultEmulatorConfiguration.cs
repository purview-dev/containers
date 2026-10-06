namespace Purview.Containers.AzureKeyVaultEmulator;

/// <summary>Immutable configuration for an Azure Key Vault Emulator test container.</summary>
public sealed record AzureKeyVaultEmulatorConfiguration : ContainerConfiguration
{
	/// <summary>True when the emulator persists vault data next to the mounted certificates.</summary>
	public bool Persist { get; init; }

	/// <summary>Host directory mounted at <see cref="AzureKeyVaultEmulatorBuilder.CertificateMountPath" />.</summary>
	public string CertificateDirectory { get; init; } = string.Empty;

	/// <summary>True when a missing certificate pair is generated instead of being required.</summary>
	public bool GenerateCertificates { get; init; } = true;

	/// <summary>True when the certificates are installed into the host trust store.</summary>
	public bool InstallCertificatesIntoTrustStore { get; init; } = true;

	/// <summary>True when the certificates are uninstalled and deleted on dispose.</summary>
	public bool CleanupCertificatesOnDispose { get; init; }
}
