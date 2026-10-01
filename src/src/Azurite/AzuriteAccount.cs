namespace Purview.Containers.Azurite;

/// <summary>Well-known Azurite emulator account details.</summary>
public static class AzuriteAccount
{
	/// <summary>The well-known Azurite storage account name.</summary>
	public const string Name = "devstoreaccount1";

	// The well-known Azurite devstoreaccount1 key is intentionally NOT embedded in this library.
	// The consuming repository supplies it here (or via a runtime configuration point) before
	// authenticated operations are exercised.
	public const string Key = "<azurite-devstoreaccount1-key-placeholder>";
}
