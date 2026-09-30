# Purview.WslContainers.Redis

Throwaway Redis-compatible instances for .NET integration testing, running as WSLC containers on
**Microsoft WSL Containers** — no Docker installation.

```bash
dotnet add package Purview.WslContainers.Redis
```

Depends on `Purview.WslContainers` (the core runtime). `StackExchange.Redis` is not referenced by this
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
using Purview.WslContainers.Redis;
using StackExchange.Redis;

await using var redis = new RedisBuilder().Build();

await redis.StartAsync();   // waits for redis-cli ping

using var connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
await connection.GetDatabase().PingAsync();
```

## API

| Member | Purpose |
| -- | -- |
| `RedisBuilder()` / `RedisBuilder(string image)` | Default image `redis:7`, or a custom image. |
| `RedisBuilder.RedisPort` (6379) | Container port, mapped to a random host port. |
| `RedisContainer.GetConnectionString()` | `localhost:{mappedPort}` for `ConnectionMultiplexer.ConnectAsync`. |

The default wait strategy runs `redis-cli ping` inside the container unless you supply your own with
`WithWaitStrategy(...)`. Call `GetConnectionString()` after `StartAsync()`.

## Redis-compatible images

The module is a thin Redis client-neutral wrapper, so other Redis-compatible servers work through the image
constructor:

```csharp
await using var valkey = new RedisBuilder("valkey/valkey:7").Build();
await using var garnet = new RedisBuilder("ghcr.io/microsoft/garnet:latest").Build();
```

## Documentation

- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
