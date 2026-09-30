using Purview.WslContainers.Azurite;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.Azurite.IntegrationTests;

public class AzuriteIntegrationTests
{
	[Test]
	public async Task Azurite_StartsAndExposesEndpoints()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using AzuriteContainer azurite = new AzuriteBuilder().Build();

		await azurite.StartAsync();

		await WslcTest.WaitForTcpAsync(azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort), TimeSpan.FromSeconds(30));
		await WslcTest.WaitForTcpAsync(azurite.GetMappedPublicPort(AzuriteBuilder.QueuePort), TimeSpan.FromSeconds(30));
		await WslcTest.WaitForTcpAsync(azurite.GetMappedPublicPort(AzuriteBuilder.TablePort), TimeSpan.FromSeconds(30));
	}

	[Test]
	public async Task ConnectionString_UsesMappedPortsAndAccount()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using AzuriteContainer azurite = new AzuriteBuilder().Build();

		await azurite.StartAsync();

		ushort blobPort = azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
		ushort queuePort = azurite.GetMappedPublicPort(AzuriteBuilder.QueuePort);
		ushort tablePort = azurite.GetMappedPublicPort(AzuriteBuilder.TablePort);
		string connectionString = azurite.GetConnectionString();

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
