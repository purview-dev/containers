namespace Purview.Containers.Azurite;

/// <summary>Well-known Azurite emulator account details.</summary>
public static class AzuriteAccount
{
	/// <summary>The well-known Azurite storage account name.</summary>
	public const string Name = "devstoreaccount1";

	/// <summary>
	/// The well-known Azurite <c>devstoreaccount1</c> key. It is a published constant of the emulator (the
	/// same value the Azure Storage Emulator used), not a credential: it only ever authenticates against a
	/// local emulator.
	/// </summary>
	public const string Key =
		"Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
}
