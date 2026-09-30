# Purview.Containers.Azurite

Throwaway [Azurite](https://github.com/Azure/Azurite) (Azure Storage emulator) instances for .NET
integration testing on **WSL Containers (WSLC)** or **Docker**.

```bash
dotnet add package Purview.Containers.Azurite
```

Backend-neutral: depends on `Purview.Containers` and needs a backend package (`Purview.Containers.Wsl` or `Purview.Containers.Docker`).
See the [Getting Started guide](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Getting-Started.md).

## Requirements

- **WSL Containers backend:** Windows 10/11 with WSL Containers (`wsl --install --no-distribution`), and a
  consuming project that is a .NET 11 project targeting Windows specifically
  (`net11.0-windows10.0.19041.0`, x64 or arm64). The `Purview.Containers.Wsl` package is Windows-only and
  supplies `buildTransitive` defaults for `WindowsSdkPackageVersion`/`PlatformTarget`, rejecting an
  unsupported consumer with `PCC0001`/`PCC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Docker backend:** any reachable Docker daemon (`docker info`), with a `net10.0` or later project on any
  platform. No Windows target framework and no `PCC` guards apply.
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

## Quick start

```csharp
using Purview.Containers.Azurite;

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
not require the key). See [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md)
for the module contract.
