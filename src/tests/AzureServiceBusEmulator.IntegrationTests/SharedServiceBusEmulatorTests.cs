using Azure.Messaging.ServiceBus;

namespace Purview.Containers.AzureServiceBusEmulator;

// These tests deliberately run in parallel against one emulator: each owns its entities, so they never
// see each other's messages. This is the pattern the package README recommends.
[ClassDataSource<SharedServiceBusEmulator>(Shared = SharedType.PerTestSession)]
public class SharedServiceBusEmulatorTests(SharedServiceBusEmulator serviceBus)
{
	[Test]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task SharedEmulator_QueuePerTest_ReceivesOnlyItsOwnMessages(int testNumber)
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var queue = await serviceBus.CreateQueueAsync();
		await using var sender = serviceBus.Client.CreateSender(queue.Name);
		await using var receiver = serviceBus.Client.CreateReceiver(queue.Name);

		var body = $"message-{testNumber}";
		await sender.SendMessageAsync(new ServiceBusMessage(body));

		var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));
		var unexpected = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));

		await Assert.That(message).IsNotNull();
		await Assert.That(message!.Body.ToString()).IsEqualTo(body);
		await Assert.That(unexpected).IsNull();
	}

	[Test]
	public async Task SharedEmulator_TopicPerTest_DeliversToItsSubscription()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var topic = await serviceBus.CreateTopicAsync("all");
		await using var sender = serviceBus.Client.CreateSender(topic.Name);
		await using var receiver = serviceBus.Client.CreateReceiver(topic.Name, "all");

		await sender.SendMessageAsync(new ServiceBusMessage("published"));

		var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));

		await Assert.That(message).IsNotNull();
		await Assert.That(message!.Body.ToString()).IsEqualTo("published");
	}
}
