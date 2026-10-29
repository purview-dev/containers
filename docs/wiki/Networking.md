# Networking

Containers default to `ContainerNetworkingMode.Bridged`. The default WSLC bridge supports
container-to-container IP traffic but not container-name DNS. `None` disables networking.

## Managed networks and alias DNS

Both backends can create an owned bridge network:

```csharp
var backend = await ContainerBackends.ResolveAsync(ct);
await using var network = await backend.CreateNetworkAsync(cancellationToken: ct);
await using var database = new ContainerBuilder(backend)
    .WithImage("redis:7")
    .WithNetwork(network.Name)
    .WithNetworkAliases("cache")
    .Build();
await database.StartAsync(ct);
```

Attach another container using the same backend and `WithNetwork(network.Name)` to reach
`cache:6379`. Dispose containers before disposing the network. Names/aliases must not contain secrets.
Network aliases require an explicit network; a named network cannot be combined with `None`.

Docker uses Testcontainers networks. WSLC uses the `wslc` CLI in the SDK-owned session because the
managed SDK exposes only bridge/none settings, not network creation. This path requires `wslc` on
PATH; the ordinary default-bridge path remains SDK-only. See
[Capabilities and Cancellation](Backend-Capabilities.md) for timeouts and platform limits.

## Host connections and IPs

- WSLC host bindings are IPv4 loopback only. Clients should use `127.0.0.1`, not `localhost`:
  IPv6-first clients, notably SQL Server clients, may not fall back.
- Docker bindings default to `127.0.0.1`; explicit `PortBinding.HostAddress` and TCP/UDP protocol
  selections are forwarded to Docker.
- Random host ports are allocated by the runtime, never by probing for an unused port.
- `GetMappedPublicPort` returns the assigned host port. TCP/HTTP readiness checks use the mapped
  host port on IPv4 loopback.
- `NetworkIp` is read after startup for the selected network, and is null before start or without a
  network. Prefer explicit aliases for container-to-container traffic on managed networks.
- Built-in module `ConnectionMode.Container` providers remain unimplemented; use explicit aliases
  and service ports to construct such connection strings.

UDP mapping remains unsupported by the WSLC public contract. Dynamic network attachment and arbitrary
network drivers are not exposed by the shared API.
