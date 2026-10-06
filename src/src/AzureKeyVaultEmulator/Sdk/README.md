# Purview.Containers.AzureKeyVaultEmulator

Throwaway [Azure Key Vault Emulator](https://github.com/james-gould/azure-keyvault-emulator) instances
for .NET integration testing on **WSL Containers (WSLC)** or **Docker**. The emulator serves the full
Key Vault REST API — secrets, keys and certificates — so the Azure SDK clients work against it unchanged.

```bash
dotnet add package Purview.Containers.AzureKeyVaultEmulator
```

Backend-neutral: depends on `Purview.Containers.Core` and needs a backend package (`Purview.Containers.Wsl`
or `Purview.Containers.Docker`). The Azure SDK client packages are referenced by this package so the
container can hand out clients that are already wired for the emulator.

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
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.
- The emulator is not a replacement for Azure Key Vault; it exists to make developing against it easier.

## Quick start

```csharp
using Purview.Containers.AzureKeyVaultEmulator;

await using var emulator = new AzureKeyVaultEmulatorBuilder().Build();

await emulator.StartAsync();   // waits for the HTTPS listener

var secretClient = emulator.GetSecretClient();
await secretClient.SetSecretAsync("mySecret", "myValue");

var secret = await secretClient.GetSecretAsync("mySecret");
```

## Certificates and trust

The Azure SDK enforces HTTPS, so the emulator must be started with a trusted certificate. The module does
the same as the emulator's own TestContainers module, fully automated:

- A self-signed `CN=localhost` certificate (SANs `localhost`, `127.0.0.1`, `host.docker.internal`) is
  generated into a per-user directory (`<profile>/keyvaultemulator/certs`; the temp directory on CI
  runners) and reused between runs, then mounted read-only at `/certs`.
- By default the certificate is installed into the host trust store (Windows: `CurrentUser\Root`; Linux:
  `/usr/local/share/ca-certificates` + `update-ca-certificates`, which needs `sudo`; macOS: the command to
  run is printed). Windows may prompt once to confirm the installation.
- The clients the module returns **pin the emulator certificate** through their HTTP transport, so they
  work even when trust-store installation is skipped. Call `WithTrustStoreInstallation(false)` to leave the
  host untouched — the recommended setting for CI.

```csharp
await using var emulator = new AzureKeyVaultEmulatorBuilder()
    .WithTrustStoreInstallation(false)   // never modify the host (recommended for CI)
    .WithCertificateCleanup()            // remove the certificate again on dispose
    .Build();
```

Bring your own certificates with `WithCertificateDirectory(path)`: the directory must contain
`emulator.pfx` (password `emulator`, a fixed requirement of the image) and `emulator.crt`. Pair it with
`WithGeneratedCertificates(false)` to require the files rather than generate them.

## Persistence

`WithPersistence()` writes an `emulator.db` next to the certificates, so vault data survives between runs.
Persisted data embeds the vault URI, so it requires a fixed host port — enabling persistence without one
throws at `Build()`:

```csharp
await using var emulator = new AzureKeyVaultEmulatorBuilder()
    .WithPersistence()
    .WithFixedHostPort(4997)
    .Build();
```

## API

| Member | Purpose |
| -- | -- |
| `AzureKeyVaultEmulatorBuilder()` / `(string image)` / `(IContainerBackend)` | Default image `jamesgoulddev/azure-keyvault-emulator:3.1.3` (`3.1.3-arm` on ARM64), or a custom image. |
| `AzureKeyVaultEmulatorBuilder.EmulatorPort` (4997) | Container port, mapped to a random host port unless pinned. |
| `WithPersistence()` / `WithFixedHostPort(ushort)` | Persist vault data; pin the host port persistence requires. |
| `WithCertificateDirectory(string)` / `WithGeneratedCertificates(bool)` | Supply the certificate pair, or control generation. |
| `WithTrustStoreInstallation(bool)` / `WithCertificateCleanup(bool)` | Control host trust-store installation and dispose-time cleanup. |
| `AzureKeyVaultEmulatorContainer.GetVaultUri()` | `https://127.0.0.1:{mappedPort}` — the vault URI. |
| `AzureKeyVaultEmulatorContainer.GetConnectionString()` | The vault URI (so `IContainer.GetConnectionString()` works too). |
| `AzureKeyVaultEmulatorContainer.GetCertificate()` | The certificate the emulator serves. |
| `GetSecretClient()` / `GetKeyClient()` / `GetCertificateClient()` | Azure SDK clients wired for the emulator. |

The default wait strategy probes `GET /` over HTTPS (ignoring the self-signed certificate) and is replaced
when you supply your own with `WithWaitStrategy(...)`. Call the accessors after `StartAsync()`, once the
mapped port is known.

## Using your own clients

Build clients against `GetVaultUri()` with any credential; the emulator accepts any bearer token. When the
certificate is installed into the trust store, the default client options work. Otherwise set
`DisableChallengeResourceVerification = true` and supply a transport that accepts the certificate from
`GetCertificate()`.

## Documentation

- [Backends: WSLC or Docker](https://github.com/purview-dev/containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
- [Azure Key Vault Emulator](https://github.com/james-gould/azure-keyvault-emulator) — the emulator itself.
