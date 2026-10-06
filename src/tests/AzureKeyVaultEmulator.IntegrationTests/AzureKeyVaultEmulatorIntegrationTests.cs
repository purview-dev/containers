using System.Globalization;

namespace Purview.Containers.AzureKeyVaultEmulator;

// Trust-store installation is disabled: the module's own clients pin the emulator certificate, so the
// tests never modify the host trust store.
public class AzureKeyVaultEmulatorIntegrationTests
{
	[Test]
	public async Task Emulator_StoresAndReturnsASecret()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var emulator = new AzureKeyVaultEmulatorBuilder().WithTrustStoreInstallation(false).Build();

		await emulator.StartAsync();

		var client = emulator.GetSecretClient();
		await client.SetSecretAsync("integration-secret", "s3cret");

		var secret = await client.GetSecretAsync("integration-secret");

		await Assert.That(secret.Value.Value).IsEqualTo("s3cret");
	}

	[Test]
	public async Task Emulator_VaultUriUsesTheMappedHttpsPort()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var emulator = new AzureKeyVaultEmulatorBuilder().WithTrustStoreInstallation(false).Build();

		await emulator.StartAsync();

		var port = emulator.GetMappedPublicPort(AzureKeyVaultEmulatorBuilder.EmulatorPort);

		await Assert.That(emulator.GetVaultUri().Scheme).IsEqualTo("https");
		await Assert.That(emulator.GetVaultUri().Port).IsEqualTo(port);
		await Assert.That(emulator.GetConnectionString()).Contains(port.ToString(CultureInfo.InvariantCulture));
	}
}
