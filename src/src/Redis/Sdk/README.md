# Purview.Containers.Redis

Throwaway Redis-compatible instances for .NET integration testing on **WSL Containers (WSLC)** or **Docker**.

```bash
dotnet add package Purview.Containers.Redis
```

Backend-neutral: depends on `Purview.Containers` and needs a backend package (`Purview.Containers.Wsl` or `Purview.Containers.Docker`). `StackExchange.Redis` is not referenced by this
package — bring your own client.

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
using Purview.Containers.Redis;
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

- [Backends: WSLC or Docker](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Backends.md) — choosing and configuring the runtime.
- [Modules](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Modules.md) — the module contract and readiness choices.
- [Wait Strategies](https://github.com/purview-dev/wsl-containers/blob/main/docs/wiki/Wait-Strategies.md) — overriding readiness checks.
