# Purview.WslContainers.Azurite

Throwaway [Azurite](https://github.com/Azure/Azurite) (Azure Storage emulator) instances for .NET
integration testing, running as WSLC containers on **Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.Azurite
```

Depends on `Purview.WslContainers` (the core runtime) and needs Windows with WSL ≥ 2.9.3 (WSL Containers).
See the [Getting Started guide](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Getting-Started.md).

## Quick start

```csharp
using Purview.WslContainers.Azurite;

await using var azurite = new AzuriteBuilder().Build();

await azurite.StartAsync();   // waits for the blob/queue/table listeners

Uri blob = azurite.GetBlobEndpoint();
string connectionString = azurite.GetConnectionString();
```

## API

| Member | Purpose |
| -- | -- |
| `AzuriteBuilder()` / `AzuriteBuilder(string image)` | Default image `mcr.microsoft.com/azure-storage/azurite`, or a custom image. |
| `AzuriteBuilder.BlobPort` (10000), `QueuePort` (10001), `TablePort` (10002) | Container service ports; all three are mapped to random host ports. |
| `AzuriteContainer.GetBlobEndpoint()`, `GetQueueEndpoint()`, `GetTableEndpoint()` | Service endpoints (`http://127.0.0.1:{mappedPort}/devstoreaccount1`). |
| `AzuriteContainer.GetConnectionString()` | Azure Storage connection string for the emulator. |
| `AzuriteAccount.Name` | The well-known account name `devstoreaccount1`. |

The container starts Azurite with `--blobHost 0.0.0.0 --queueHost 0.0.0.0 --tableHost 0.0.0.0` and waits
for the `successfully listening` log signal. Endpoints and the connection string are only meaningful after
`StartAsync()` because they resolve the mapped host ports.

## Account key

The well-known `devstoreaccount1` key is a published constant of the emulator, but this library does not
embed it: `AzuriteAccount.Key` holds a placeholder. Supply the real key in your test infrastructure before
exercising authenticated operations (anonymous/local development paths are unaffected when the client does
not require the key). See [Modules](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Modules.md)
for the module contract.
