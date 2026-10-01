# Purview.Containers.Nats

Throwaway NATS brokers for .NET integration testing on **WSL Containers (WSLC)** or **Docker**.

```bash
dotnet add package Purview.Containers.Nats
```

Backend-neutral: depends on `Purview.Containers` and needs a backend package (`Purview.Containers.Wsl` or `Purview.Containers.Docker`). `NATS.Client.Core` is not referenced by this
package — bring your own client.

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

## Quick start

```csharp
using Purview.Containers.Nats;
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

- [Backends: WSLC or Docker](https://github.com/purview-dev/containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
