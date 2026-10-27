using System.Globalization;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace Purview.Containers.AzureServiceBusEmulator;

// Each test starts the emulator and its SQL Server sidecar, so the suite runs serially to keep the
// session VM (or Docker daemon) from starting several SQL Server instances at once.
[NotInParallel]
public class AzureServiceBusEmulatorIntegrationTests
{
	const string QueueName = "integration-queue";

	const string Config = """
		{
		  "UserConfig": {
		    "Namespaces": [
		      {
		        "Name": "sbemulatorns",
		        "Queues": [
		          {
		            "Name": "integration-queue",
		            "Properties": {
		              "DeadLetteringOnMessageExpiration": false,
		              "DefaultMessageTimeToLive": "PT1H",
		              "DuplicateDetectionHistoryTimeWindow": "PT20S",
		              "ForwardDeadLetteredMessagesTo": "",
		              "ForwardTo": "",
		              "LockDuration": "PT1M",
		              "MaxDeliveryCount": 3,
		              "RequiresDuplicateDetection": false,
		              "RequiresSession": false
		            }
		          }
		        ],
		        "Topics": []
		      }
		    ],
		    "Logging": { "Type": "File" }
		  }
		}
		""";

	[Test]
	public async Task Emulator_SendsAndReceivesAMessage()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var emulator = new AzureServiceBusEmulatorBuilder().AcceptLicense().WithConfig(Config).Build();

		await emulator.StartAsync();

		await using ServiceBusClient client = new(emulator.GetConnectionString());
		await using var sender = client.CreateSender(QueueName);
		await sender.SendMessageAsync(new ServiceBusMessage("hello"));

		await using var receiver = client.CreateReceiver(QueueName);
		var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));

		await Assert.That(message).IsNotNull();
		await Assert.That(message!.Body.ToString()).IsEqualTo("hello");
	}

	[Test]
	public async Task Emulator_ManagementConnectionString_ReachesTheAdministrationAPI()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var emulator = new AzureServiceBusEmulatorBuilder().AcceptLicense().WithConfig(Config).Build();

		await emulator.StartAsync();

		ServiceBusAdministrationClient administration = new(emulator.GetManagementConnectionString());
		var exists = await administration.QueueExistsAsync(QueueName);

		await Assert.That(exists.Value).IsTrue();
	}

	[Test]
	public async Task Emulator_QueueCreatedAtRuntime_SendsAndReceives()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var emulator = new AzureServiceBusEmulatorBuilder().AcceptLicense().Build();

		await emulator.StartAsync();

		// A queue per test is how one shared emulator isolates concurrent tests.
		var queueName = $"runtime-{Guid.NewGuid():N}";
		ServiceBusAdministrationClient administration = new(emulator.GetManagementConnectionString());
		await administration.CreateQueueAsync(queueName);

		await using ServiceBusClient client = new(emulator.GetConnectionString());
		await using var sender = client.CreateSender(queueName);
		await sender.SendMessageAsync(new ServiceBusMessage("runtime"));

		await using var receiver = client.CreateReceiver(queueName);
		var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));

		await Assert.That(message).IsNotNull();
		await Assert.That(message!.Body.ToString()).IsEqualTo("runtime");
	}

	[Test]
	public async Task ConnectionStrings_UseTheMappedPorts()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var emulator = new AzureServiceBusEmulatorBuilder().AcceptLicense().Build();

		await emulator.StartAsync();

		var amqpPort = emulator.GetMappedPublicPort(AzureServiceBusEmulatorBuilder.AmqpPort);
		var managementPort = emulator.GetMappedPublicPort(AzureServiceBusEmulatorBuilder.ManagementPort);

		await Assert
			.That(emulator.GetConnectionString())
			.Contains($"Endpoint=sb://127.0.0.1:{amqpPort.ToString(CultureInfo.InvariantCulture)};");
		await Assert.That(emulator.GetConnectionString()).Contains("UseDevelopmentEmulator=true");
		await Assert
			.That(emulator.GetManagementConnectionString())
			.Contains($"Endpoint=sb://127.0.0.1:{managementPort.ToString(CultureInfo.InvariantCulture)};");
		await Assert.That(emulator.SqlServer.NetworkIp).IsNotNull();
	}
}
