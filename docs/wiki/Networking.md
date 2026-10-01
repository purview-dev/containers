# Networking

WSLC networking behaviour (verified in Phase 0) and how `Purview.Containers` models it.

## Verified behaviour

| Mode (`ContainerNetworkingMode`) | Effect | Port mappings |
|---|---|---|
| `null` (default) | Docker `NetworkMode: "none"` — no network, no IP | ❌ `CreateContainer` throws `0x80070057` (EXP S4) |
| `None` | same as null | ❌ |
| `Bridged` | container gets `172.17.0.x` on the session bridge; host→container mapped ports work | ✅ (EXP S4/S8/S12) |

- Host bind address is **IPv4 loopback only** (`HostIp: 127.0.0.1`). IPv6 `::1` does not connect to the mapped port. (EXP S12)
- **Clients must use `127.0.0.1`, not `localhost`.** `localhost` resolves to `::1` first on .NET; most clients (Npgsql, StackExchange.Redis) fall back to IPv4, but **Microsoft.Data.SqlClient hangs on `::1` without falling back**, so the SQL Server module's connection strings use `127.0.0.1,{port}` explicitly.
- **Containers in the same session can reach each other by IP (Bridged)** — `wget http://<ip>:8080/` works. (EXP S9)
- **No name/DNS resolution between containers.** `wget http://<hostname>:8080/` and `http://<container-name>:8080/` fail with "bad address"; `/etc/hosts` contains only localhost + the container's own `IP <short-id>`. (EXP S9)
- There are **no managed network objects** (no `network create/connect` in `Microsoft.WSL.Containers`, unlike the CLI).

## Library design

1. **Default `NetworkingMode = Bridged`** for containers that bind ports (or, by default, for all containers to be useful). This is baked into `ContainerBuilder` defaults.
2. `GetMappedPublicPort(containerPort)` reads `Inspect().Ports["<port>/tcp"][0].HostPort`.
3. `GetNetworkIp()` reads `Inspect().NetworkSettings.Networks.bridge.IPAddress` (Bridged only).
4. TCP/HTTP wait strategies connect to `127.0.0.1:<hostPort>` (IPv4 only, per S12).
5. **Multi-container environments are deliberately deferred.** Because there is no native DNS/alias, a future `ContainerEnvironmentBuilder` would wire containers together by passing each other's IPs (from `GetNetworkIp()`) into configuration/env. The core API must not preclude this (it doesn't — `GetNetworkIp` + environment are enough).

## What NOT to model

- Do **not** invent Docker-style `Network` objects; the underlying semantics are just "bridged with IPs".
- Do **not** claim container-name resolution works; it does not.