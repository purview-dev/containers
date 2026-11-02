using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Purview.Containers.Wsl;
using TUnit.Core.Interfaces;

namespace Purview.Containers.AzureServiceBusEmulator;

/// <summary>
/// One emulator for the whole test session, shared through
/// <c>[ClassDataSource&lt;SharedServiceBusEmulator&gt;(Shared = SharedType.PerTestSession)]</c>. The emulator
/// drops and recreates its fixed SQL Server databases on start, so instances cannot share a database
/// server; tests share one instance instead and isolate themselves with their own entities.
/// </summary>
public sealed class SharedServiceBusEmulator : IAsyncInitializer, IAsyncDisposable
{
	AzureServiceBusEmulatorContainer? _emulator;

	/// <summary>The started emulator. Tests call <see cref="WslcTest.SkipIfUnavailableAsync" /> first.</summary>
	public AzureServiceBusEmulatorContainer Emulator =>
		_emulator
		?? throw new InvalidOperationException("WSL Containers is unavailable; the emulator was not started.");

	/// <summary>Client for the AMQP endpoint, shared by every test.</summary>
	public ServiceBusClient Client { get; private set; } = default!;

	/// <summary>Client for the management endpoint, used to create and delete per-test entities.</summary>
	public ServiceBusAdministrationClient Administration { get; private set; } = default!;

	/// <inheritdoc />
	public async Task InitializeAsync()
	{
		// Start only on a usable host, so the tests themselves skip (or fail under
		// PURVIEW_CONTAINERS_REQUIRE_BACKEND) instead of the fixture failing every test.
		WslcTest.EnsureBackendRegistered();
		var info = await WslContainerRuntime.Instance.GetInfoAsync();
		if (!info.IsAvailable || !info.IsCompatible)
		{
			return;
		}

		_emulator = new AzureServiceBusEmulatorBuilder().AcceptLicense().Build();
		await _emulator.StartAsync();

		Client = new ServiceBusClient(_emulator.GetConnectionString());
		Administration = new ServiceBusAdministrationClient(_emulator.GetManagementConnectionString());
	}

	/// <summary>Creates a uniquely named queue for one test; dispose the result to delete it.</summary>
	public async Task<TestEntity> CreateQueueAsync(string prefix = "q")
	{
		var name = UniqueName(prefix);
		await Administration.CreateQueueAsync(name);
		return new TestEntity(name, () => Administration.DeleteQueueAsync(name));
	}

	/// <summary>Creates a uniquely named topic with one subscription; dispose the result to delete both.</summary>
	public async Task<TestEntity> CreateTopicAsync(string subscription, string prefix = "t")
	{
		var name = UniqueName(prefix);
		await Administration.CreateTopicAsync(name);
		await Administration.CreateSubscriptionAsync(name, subscription);
		return new TestEntity(name, () => Administration.DeleteTopicAsync(name));
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_emulator is null)
		{
			return;
		}

		await Client.DisposeAsync();
		await _emulator.DisposeAsync();
	}

	// Short names: a GUID keeps them unique without approaching the entity name length limit.
	static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

	/// <summary>A per-test entity, deleted on dispose so the namespace stays under its entity quota.</summary>
	public sealed class TestEntity(string name, Func<Task> delete) : IAsyncDisposable
	{
		/// <summary>The entity name.</summary>
		public string Name { get; } = name;

		/// <inheritdoc />
		public async ValueTask DisposeAsync() => await delete();
	}
}
