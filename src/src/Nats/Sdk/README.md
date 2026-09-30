# Purview.WslContainers.Nats

Throwaway NATS brokers for .NET integration testing, running as WSLC containers on
**Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.Nats
```

Depends on `Purview.WslContainers` (the core runtime). `NATS.Client.Core` is not referenced by this
package — bring your own client.

## Requirements

- Windows 10/11 with **WSL Containers** (`wsl --install --no-distribution`).
- **A .NET 11 project targeting Windows specifically** (`net11.0-windows10.0.19041.0`, x64 or arm64).
  `Purview.WslContainers` supplies `buildTransitive` defaults for `WindowsSdkPackageVersion` and
  `PlatformTarget`, and rejects an unsupported consumer with `PWC0001`/`PWC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

## Quick start

```csharp
using Purview.WslContainers.Nats;
using NATS.Client.Core;

await using var nats = new NatsBuilder().Build();

await nats.StartAsync();   // waits for 'Listening for client connections'

await using var client = new NatsConnection(new NatsOpts { Url = nats.GetConnectionString() });
await client.PublishAsync("orders.created", "42");
```

## API

| Member | Purpose |
| -- | -- |
| `NatsBuilder()` / `NatsBuilder(string image)` | Default image `nats:2`, or a custom image. |
| `NatsBuilder.ClientPort` (4222), `MonitoringPort` (8222) | Container ports, both mapped to random host ports. |
| `NatsContainer.GetClientEndpoint()` | `nats://127.0.0.1:{mappedPort}` as a `Uri`. |
| `NatsContainer.GetConnectionString()` | The client endpoint as a string. |
| `NatsContainer.GetMonitoringEndpoint()` | Monitoring HTTP endpoint (`http://127.0.0.1:{mappedPort}`), e.g. `/varz`. |

The default wait strategy matches the `Listening for client connections` log line unless you supply your own
with `WithWaitStrategy(...)`. Endpoint accessors resolve the mapped host ports, so call them after
`StartAsync()`.

## Documentation

- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
