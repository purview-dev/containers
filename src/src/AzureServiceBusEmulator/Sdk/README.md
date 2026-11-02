# Purview.Containers.AzureServiceBusEmulator

Throwaway [Azure Service Bus Emulator](https://github.com/Azure/azure-service-bus-emulator-installer)
instances for .NET integration testing on **WSL Containers (WSLC)** or **Docker**. The emulator speaks
AMQP, so `ServiceBusClient` from `Azure.Messaging.ServiceBus` works against it unchanged.

```bash
dotnet add package Purview.Containers.AzureServiceBusEmulator
```

Backend-neutral: depends on `Purview.Containers.Core` and `Purview.Containers.MsSql` (the emulator stores
its state in SQL Server), and needs a backend package (`Purview.Containers.Wsl` or
`Purview.Containers.Docker`).

## Requirements

- **WSL Containers backend:** Windows 10/11 with WSL Containers (`wsl --install --no-distribution`), and a
  `.NET 10` or later project. A platform-neutral `net10.0` project binds the portable facade and gets
  automatic WSLC-or-Docker selection; a Windows target framework
  (`net10.0-windows10.0.19041.0`, x64 or arm64) binds the implementation directly. The
  `Purview.Containers.Wsl` package supplies `buildTransitive` defaults for
  `WindowsSdkPackageVersion`/`PlatformTarget` and (for a platform-neutral consumer on a Windows build
  host) the implementation payload, rejecting an unsupported consumer with `PCC0001`/`PCC0002` — see the
  [consumer requirements](https://github.com/purview-dev/containers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Docker backend:** any reachable Docker daemon (`docker info`), with a `net10.0` or later project on any
  platform. No Windows target framework and no `PCC` guards apply.
- **SQL Server sidecar:** the default is SQL Server 2025 (`mcr.microsoft.com/mssql/server:2025-latest`),
  which is x64 only and needs at least 2000 MB of memory in the WSLC session VM or Docker host. On ARM64,
  `WithSqlServerImage("mcr.microsoft.com/azure-sql-edge:latest")` is an alternative the emulator supports.
  SQL Server 2022 does not start on WSLC (it fails to create `/.system` as the non-root `mssql` user).
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.
- The emulator is for local development and testing only; it is not a replacement for Azure Service Bus.

## Quick start

```csharp
using Azure.Messaging.ServiceBus;
using Purview.Containers.AzureServiceBusEmulator;

await using var emulator = new AzureServiceBusEmulatorBuilder()
    .AcceptLicense()             // explicit: the emulator and SQL Server EULAs
    .Build();

await emulator.StartAsync();     // starts SQL Server, then the emulator; waits for GET /health

await using ServiceBusClient client = new(emulator.GetConnectionString());
await client.CreateSender("queue.1").SendMessageAsync(new ServiceBusMessage("hello"));
```

Without a configuration the image's default `Config.json` applies (namespace `sbemulatorns`, queue
`queue.1`, topic `topic.1` with subscription `subscription.1`).

## Licensing

`Build()` throws until `AcceptLicense()` is called. That single call accepts both the
[emulator EULA](https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt)
and the SQL Server EULA of the sidecar (`ACCEPT_EULA=Y` on both containers). The library never accepts
licensing terms on your behalf.

## Entities

Declare the namespace, queues, topics and subscriptions with the emulator's `Config.json` format:

```csharp
await using var emulator = new AzureServiceBusEmulatorBuilder()
    .AcceptLicense()
    .WithConfigFile("servicebus-config.json")   // or .WithConfig(json)
    .Build();
```

The configuration is written to a per-container temporary directory, mounted read-only at
`/ServiceBus_Emulator/ConfigFiles`, and deleted on dispose. See the
[emulator configuration reference](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator#configuration)
for the schema and its quotas.

## How the SQL Server sidecar is wired

WSL Containers has no container-name DNS, so the module does what both backends support: it starts a SQL
Server container (through the `MsSql` module, so readiness is a real client connection) and passes its
bridge IP (`IContainer.NetworkIp`) to the emulator as `SQL_SERVER`. Disposing the emulator disposes the
sidecar. `SqlServer` exposes the sidecar for diagnostics such as its logs.

## Sharing one emulator across tests

Starting an emulator takes roughly a minute (SQL Server, then the emulator), so start **one per test
session** and give every test its own entities. The tests can then run in parallel: a test only ever
reads from the queue or topic it created, so it never sees another test's messages.

Changing the database name does not help instead. The emulator always uses the same two databases
(`SbGatewayDatabase`, `SbMessageContainerDatabase00001`) and drops and recreates them on every start, so
two emulators pointed at one SQL Server would wipe each other.

With TUnit, share the emulator through a `ClassDataSource` fixture:

```csharp
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Purview.Containers.AzureServiceBusEmulator;
using TUnit.Core.Interfaces;

public sealed class ServiceBusEmulatorFixture : IAsyncInitializer, IAsyncDisposable
{
    public AzureServiceBusEmulatorContainer Emulator { get; } =
        new AzureServiceBusEmulatorBuilder().AcceptLicense().Build();

    public ServiceBusClient Client { get; private set; } = default!;

    public ServiceBusAdministrationClient Administration { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await Emulator.StartAsync();
        Client = new ServiceBusClient(Emulator.GetConnectionString());
        Administration = new ServiceBusAdministrationClient(Emulator.GetManagementConnectionString());
    }

    // A unique queue per test; delete it afterwards to stay under the namespace's entity quota.
    public async Task<string> CreateQueueAsync()
    {
        var name = $"q-{Guid.NewGuid():N}";
        await Administration.CreateQueueAsync(name);
        return name;
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Emulator.DisposeAsync();
    }
}

[ClassDataSource<ServiceBusEmulatorFixture>(Shared = SharedType.PerTestSession)]
public class OrderProcessingTests(ServiceBusEmulatorFixture serviceBus)
{
    [Test]
    public async Task PlacedOrder_IsPublished()
    {
        var queue = await serviceBus.CreateQueueAsync();
        try
        {
            await using var sender = serviceBus.Client.CreateSender(queue);
            await sender.SendMessageAsync(new ServiceBusMessage("order-1"));

            await using var receiver = serviceBus.Client.CreateReceiver(queue);
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));

            await Assert.That(message!.Body.ToString()).IsEqualTo("order-1");
        }
        finally
        {
            await serviceBus.Administration.DeleteQueueAsync(queue);
        }
    }
}
```

Topics work the same way: `CreateTopicAsync(name)` followed by `CreateSubscriptionAsync(name, subscription)`.
Create entities at run time rather than in `Config.json` when tests share an instance; the configuration
is fixed when the emulator starts. With xUnit, put the same start/stop code in an assembly or collection
fixture.

## Limitations

What this module and its tests have established:

- **One SQL Server per emulator.** The fixed database names (see above) mean an emulator owns its SQL
  Server outright. Share the emulator, never the database server.
- **No state survives a restart.** The databases are recreated on every start, so entities created at run
  time and any unconsumed messages are lost with the container.
- **Slow, memory-hungry start.** About a minute per instance, and the SQL Server sidecar needs at least
  2000 MB. Running several emulators at once multiplies both, which is why sharing one is recommended.
- **x64 only by default.** SQL Server 2025 has no ARM64 image.
- **Host connections only.** The connection strings point at `127.0.0.1` and the mapped host ports. An
  application under test that runs in another container cannot use them (`ConnectionMode.Container` is
  not supported).
- **Fixed development credentials.** The emulator accepts only `RootManageSharedAccessKey` /
  `SAS_KEY_VALUE`; there is no Microsoft Entra ID (token) authentication.
- **Unpinned image.** The default emulator tag is `latest`, so an upstream release can change behaviour.
  Pin a tag with `WithImage(...)` when a test run must be reproducible.
- **Its logs contain the SQL Server password.** The emulator writes its SQL connection string, password
  included, to its log. Treat those logs as sensitive.

The emulator is also not the full service. Check Microsoft's
[emulator documentation](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator) for its
current quotas (such as the number of entities per namespace and the maximum message size) and the
features it does not support, before relying on it for a given scenario.

## API

| Member | Purpose |
| -- | -- |
| `AzureServiceBusEmulatorBuilder()` / `(string image)` / `(IContainerBackend)` | Default image `mcr.microsoft.com/azure-messaging/servicebus-emulator:latest`, or a custom image. The sidecar uses the same backend. |
| `AzureServiceBusEmulatorBuilder.AmqpPort` (5672), `ManagementPort` (5300) | Container ports; both are mapped to random host ports. |
| `AcceptLicense()` | Required: accepts the emulator and SQL Server EULAs. |
| `WithConfig(string json)` / `WithConfigFile(string path)` | The emulator `Config.json`. |
| `WithSqlServerImage(string)` / `WithSqlServerPassword(string)` | Sidecar image (default `mcr.microsoft.com/mssql/server:2025-latest`) and SA password (at least 8 characters). |
| `AzureServiceBusEmulatorContainer.GetConnectionString()` | `Endpoint=sb://127.0.0.1:{amqpPort};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;` for `ServiceBusClient`. |
| `AzureServiceBusEmulatorContainer.GetManagementConnectionString()` | The same against the management port, for `ServiceBusAdministrationClient`. |
| `AzureServiceBusEmulatorContainer.SqlServer` | The SQL Server sidecar container. |

The default wait strategy polls `GET /health` on the management port and is replaced when you supply
your own with `WithWaitStrategy(...)`. Call the accessors after `StartAsync()`, once the mapped ports are
known.

## Documentation

- [Backends: WSLC or Docker](https://github.com/purview-dev/containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Networking](https://github.com/purview-dev/containers/blob/main/docs/wiki/Networking.md) — how containers reach each other by IP.
- [Azure Service Bus Emulator](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator) — the emulator itself.
