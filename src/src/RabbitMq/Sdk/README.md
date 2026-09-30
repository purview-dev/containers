# Purview.WslContainers.RabbitMq

Throwaway RabbitMQ brokers for .NET integration testing, running as WSLC containers on
**Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.RabbitMq
```

Depends on `Purview.WslContainers` (the core runtime). `RabbitMQ.Client` is not referenced by this package —
bring your own client.

## Requirements

- Windows 10/11 with **WSL Containers** (`wsl --install --no-distribution`).
- **A .NET 11 project targeting Windows specifically** (`net11.0-windows10.0.19041.0`, x64 or arm64).
  `Purview.WslContainers` supplies `buildTransitive` defaults for `WindowsSdkPackageVersion` and
  `PlatformTarget`, and rejects an unsupported consumer with `PWC0001`/`PWC0002` — see the
  [consumer requirements](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Consumer-Requirements.md).
- **Experimental:** the API, defaults and packaging can change between prereleases; there is no
  production support guarantee.

## Quick start

```csharp
using Purview.WslContainers.RabbitMq;
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

`Build()` rejects an empty username, password or virtual host with `WslContainerConfigurationException`.
Call the endpoint accessors after `StartAsync()`.

## Readiness

The default wait strategy matches the canonical `Server startup complete` log line rather than running
`rabbitmq-diagnostics ping`: under WSLC an exec-based probe races the Erlang cookie setup and can trigger a
startup failure (`eacces` reading `.erlang.cookie`). See
[Modules](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Modules.md) for the full note.

## Documentation

- [Modules](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-testcontainers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
