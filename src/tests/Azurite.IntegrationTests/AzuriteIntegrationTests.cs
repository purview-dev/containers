using Azure.Storage.Blobs;

namespace Purview.Containers.Azurite;

[NotInParallel]
public class AzuriteIntegrationTests
{
	[Test]
	public async Task ConnectionString_AuthenticatesWithTheWellKnownAccountKey()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var azurite = new AzuriteBuilder().Build();

		await azurite.StartAsync();

		// Creating a container is a shared-key-signed request: Azurite rejects it unless the key is right.
		// The service version is pinned because the SDK can be ahead of what the Azurite image supports.
		BlobServiceClient client = new(
			azurite.GetConnectionString(),
			new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
		);
		var container = await client.CreateBlobContainerAsync("authenticated");

		var exists = await container.Value.ExistsAsync();

		await Assert.That(exists.Value).IsTrue();
	}

	[Test]
	public async Task Azurite_StartsAndExposesEndpoints()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var azurite = new AzuriteBuilder().Build();

		await azurite.StartAsync();

		await WslcTest.WaitForTcpAsync(azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort), TimeSpan.FromSeconds(30));
		await WslcTest.WaitForTcpAsync(azurite.GetMappedPublicPort(AzuriteBuilder.QueuePort), TimeSpan.FromSeconds(30));
		await WslcTest.WaitForTcpAsync(azurite.GetMappedPublicPort(AzuriteBuilder.TablePort), TimeSpan.FromSeconds(30));
	}

	[Test]
	public async Task ConnectionString_UsesMappedPortsAndAccount()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var azurite = new AzuriteBuilder().Build();

		await azurite.StartAsync();

		var blobPort = azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
		var queuePort = azurite.GetMappedPublicPort(AzuriteBuilder.QueuePort);
		var tablePort = azurite.GetMappedPublicPort(AzuriteBuilder.TablePort);
		var connectionString = azurite.GetConnectionString();

		await Assert.That(connectionString).Contains("DefaultEndpointsProtocol=http");
		await Assert.That(connectionString).Contains("AccountName=devstoreaccount1");
		await Assert.That(connectionString).Contains($"BlobEndpoint=http://127.0.0.1:{blobPort}/devstoreaccount1");
		await Assert.That(connectionString).Contains($"QueueEndpoint=http://127.0.0.1:{queuePort}/devstoreaccount1");
		await Assert.That(connectionString).Contains($"TableEndpoint=http://127.0.0.1:{tablePort}/devstoreaccount1");
		await Assert
			.That(azurite.GetBlobEndpoint().ToString())
			.IsEqualTo($"http://127.0.0.1:{blobPort}/devstoreaccount1");
		await Assert
			.That(azurite.GetQueueEndpoint().ToString())
			.IsEqualTo($"http://127.0.0.1:{queuePort}/devstoreaccount1");
		await Assert
			.That(azurite.GetTableEndpoint().ToString())
			.IsEqualTo($"http://127.0.0.1:{tablePort}/devstoreaccount1");
	}
}
