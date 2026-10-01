# Purview.Containers

The **umbrella package**: one reference that brings the abstractions and both backends, so the same tests
run on **WSL Containers (WSLC)** on a Windows machine and on **Docker** everywhere else — no backend
choice in your code or environment.

```bash
dotnet add package Purview.Containers
```

It depends on:

- [`Purview.Containers.Core`](https://www.nuget.org/packages/Purview.Containers.Core) — the backend-neutral
  abstractions (`IContainer`, `ContainerBuilder`, `ContainerBackends`, wait strategies, …; public namespace
  `Purview.Containers`).
- [`Purview.Containers.Wsl`](https://www.nuget.org/packages/Purview.Containers.Wsl) — the WSL Containers
  backend.
- [`Purview.Containers.Docker`](https://www.nuget.org/packages/Purview.Containers.Docker) — the Docker
  backend (Testcontainers).

## Quick start

A service module plus this package is all you need:

```bash
dotnet add package Purview.Containers.Redis
dotnet add package Purview.Containers
```

```csharp
using Purview.Containers.Redis;
using StackExchange.Redis;

await using var redis = new RedisBuilder().Build();
await redis.StartAsync();                                   // waits for redis-cli ping

await using var cache = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
await cache.GetDatabase().PingAsync();
```

There is no registration line and no `PURVIEW_CONTAINERS_BACKEND`: each backend package ships
`buildTransitive` assets that register themselves in your assembly, and automatic selection picks the
first usable backend (WSLC is preferred where it works, Docker otherwise). Use a **`net10.0`** (or later)
project — see [Using it in your tests](https://github.com/purview-dev/containers/blob/main/docs/wiki/Using-in-Your-Tests.md).

## When to use this versus a single backend

| You want | Reference |
| --- | --- |
| WSLC locally **and** Docker in CI, one project, zero config | `Purview.Containers` (this package) |
| Docker only (e.g. a Linux-only CI job) — avoids the ~19 MB WSLC payload | `Purview.Containers.Core` + `Purview.Containers.Docker` |
| WSLC only | `Purview.Containers.Core` + `Purview.Containers.Wsl` |

Adding a future backend (for example Podman) means adding its package to this bundle; your project does
not change.

## Requirements

- **.NET 10 or later.** The abstractions and service modules are portable; the WSL Containers backend is
  multi-target and stays dormant on non-Windows hosts. See the
  [consumer requirements](https://github.com/purview-dev/containers/blob/main/docs/wiki/Consumer-Requirements.md).
- Either a Windows 10/11 host with **WSL Containers** (`wsl --install --no-distribution`) or a reachable
  **Docker** daemon.

## Documentation

- [Using it in your tests (auto)](https://github.com/purview-dev/containers/blob/main/docs/wiki/Using-in-Your-Tests.md)
- [Backends: WSLC or Docker](https://github.com/purview-dev/containers/blob/main/docs/wiki/Backends.md)
- [Modules](https://github.com/purview-dev/containers/blob/main/docs/wiki/Modules.md)

> **Experimental.** The public API, defaults and packaging rules can change between prereleases; there is
> no production support guarantee.