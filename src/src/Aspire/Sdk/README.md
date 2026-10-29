# Purview.Containers.Aspire

> **Experimental.** Pin the exact prerelease version; the API and defaults may change.

Apply verified container bind-source resolution to any Aspire AppHost.

```csharp
var builder = DistributedApplication.CreateBuilder(args);
builder.AddContainerEnvironment();

builder.AddContainer("example", "your-image")
    .WithBindMount(localDirectory, "/data", isReadOnly: true);

await builder.Build().RunAsync();
```

The adapter uses Aspire's selected `ASPIRE_CONTAINER_RUNTIME` (default `docker`), and the underlying
CLI retains its Docker context and TLS configuration. Call once during builder configuration.
Registration is idempotent.

Resolution happens at `BeforeResourceStartedEvent`, after application startup hooks have added
their mounts and before DCP renders the container specification. Only bind sources are replaced:
named volumes, target paths, read-only flags, endpoints and publish-mode manifests stay unchanged.
Keep local source directories/files present before starting the container.

Supply a `Purview.Containers.Environment.ContainerEnvironmentResolver` instance as the second argument for
explicit mappings or a custom probe timeout/image. Otherwise the core package's environment
configuration and verified native/WSL discovery are used.

See the [environment package README](https://github.com/purview-dev/containers/blob/main/src/src/Environment/Sdk/README.md)
for supported environments, marker permissions, offline probe images and remote-filesystem limits.
This adapter does not configure remote service routing; retain Aspire's endpoint discovery and prove
reachability with real service requests.
