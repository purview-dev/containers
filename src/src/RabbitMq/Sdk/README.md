# Purview.Containers.RabbitMq

Throwaway RabbitMQ brokers for .NET integration testing on **WSL Containers (WSLC)** or **Docker**.

```bash
dotnet add package Purview.Containers.RabbitMq
```

Backend-neutral: depends on `Purview.Containers` and needs a backend package (`Purview.Containers.Wsl` or `Purview.Containers.Docker`). `RabbitMQ.Client` is not referenced by this package —
bring your own client.

## Requirements

- **WSL Containers backend:** Windows 10/11 with WSL Containers (`wsl --install --no-distribution`), and a
  `.NET 10` or later project. A platform-neutral `net10.0` project binds the portable facade and gets
  automatic WSLC-or-Docker selection; a Windows target framework
  (`net10.0-windows10.0.19041.0`, x64 or arm64) binds the implementation directly. The
  `Purview.Containers.Wsl` package supplies `buildTransitive` defaults for
  `WindowsSdkPackageVersion`/`PlatformTarget` and (for a platform-neutral consumer on a Windows build
  host) the implementation payload, rejecting an unsupported consumer with `PCC0001`/`PCC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Docker backend:** any reachable Docker daemon (`docker info`), with a `net10.0` or later project on any
  platform. No Windows target framework and no `PCC` guards apply.
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

## Quick start

```csharp
using Purview.Containers.RabbitMq;
using RabbitMQ.Client;

await using var rabbitMq = new RabbitMqBuilder()
    .WithUsername("guest")
    .WithPassword("guest")
    .Build();

await rabbitMq.StartAsync();   // waits for 'Server startup complete'

var factory = new ConnectionFactory { Uri = rabbitMq.GetAmqpEndpoint() };
await using var connection = await factory.CreateConnectionAsync();
```

## API

| Member | Purpose |
| -- | -- |
| `RabbitMqBuilder()` / `RabbitMqBuilder(string image)` | Default image `rabbitmq:3-management` (management plugin included), or a custom image. |
| `RabbitMqBuilder.AmqpPort` (5672), `ManagementPort` (15672) | Container ports, both mapped to random host ports. |
| `WithUsername(string)` | Sets `RABBITMQ_DEFAULT_USER` (default `guest`). |
| `WithPassword(string)` | Sets `RABBITMQ_DEFAULT_PASS` (default `guest`); stored as a redacted `Secret`. |
| `WithVirtualHost(string)` | Sets `RABBITMQ_DEFAULT_VHOST` (default `/`). |
| `RabbitMqContainer.GetAmqpEndpoint()` | `amqp://user:pass@localhost:{port}/{vhost}` as a `Uri` for `ConnectionFactory.Uri`. |
| `RabbitMqContainer.GetConnectionString()` | The same AMQP endpoint as a string. |
| `RabbitMqContainer.GetManagementEndpoint()` | Management web UI endpoint (`http://localhost:{mappedPort}`). |

`Build()` rejects an empty username, password or virtual host with `ContainerConfigurationException`.
Call the endpoint accessors after `StartAsync()`.

## Readiness

The default wait strategy matches the canonical `Server startup complete` log line rather than running
`rabbitmq-diagnostics ping`: under WSLC an exec-based probe races the Erlang cookie setup and can trigger a
startup failure (`eacces` reading `.erlang.cookie`). See
[Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) for the full note.

## Documentation

- [Backends: WSLC or Docker](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
