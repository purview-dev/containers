# Backend capabilities and cancellation

The Docker backend is implemented with Testcontainers for .NET. WSLC uses
`Microsoft.WSL.Containers` for ordinary lifecycle operations and the `wslc` CLI for capabilities
missing from the managed SDK. Neither backend silently switches runtimes to implement a feature.
This is a Testcontainers-style API, not a source-compatible replacement for every Testcontainers API.
Supply the command explicitly when overriding an entrypoint; Docker-style entrypoint overrides
do not preserve an image's default command.

## Shared capabilities

| Capability | API | WSLC implementation |
| --- | --- | --- |
| Image command and environment | `WithCommand`, `WithEnvironment` | SDK normally |
| Entrypoint, user, labels | `WithEntrypoint`, `WithUser`, `WithLabel` | CLI creation, SDK handle reopened before startup |
| Exec overrides | `ExecOptions.Environment`, `WorkingDirectory`, `Timeout` | SDK process |
| Pre-start files/directories | `WithResourceMapping(source, target)` | CLI tar copy before init starts |
| Runtime files/directories | `CopyAsync(source, target)`, `ReadFileAsync(path)` | CLI tar copy; reads return exact bytes |
| One-shot containers | `Wait.ForExitCode(expected)`, `WaitForExitAsync(ct)` | Live init-process state/exit code |
| Managed bridge networks and aliases | `CreateNetworkAsync`, `WithNetwork`, `WithNetworkAliases` | CLI network and container creation; alias DNS |
| Dockerfile builds | `BuildImageAsync` | CLI build in the owning SDK session/store |
| Reuse | `WithName(...).WithReuse()` | Same configuration/name within the owning session |
| Explicit deletion | `DeleteAsync(ct)` | Force-delete, including reusable containers |

Use absolute Linux destinations for copy/mapping operations. A file source names the destination
file; a directory source copies its contents beneath the destination directory. `ReadFileAsync`
reads a regular file, not a directory archive. Prefer resource copy over host bind mounts when a
remote daemon cannot see the test host filesystem.
Empty directories are preserved, including in mappings copied before init starts.

## Bounded defaults

| Operation | Default | Configuration |
| --- | --- | --- |
| Startup: backend resolution, image, create, start, readiness | 5 minutes total | `WithStartupTimeout` |
| Exec and init-exit wait | 1 minute | `WithExecTimeout`; individual `ExecOptions.Timeout` |
| Stop, including force-kill escalation | 15 seconds | `WithStopTimeout` |
| Disposal/deletion | 30 seconds | `WithCleanupTimeout` |
| File operations and log snapshots | 1 minute | `WithOperationTimeout` |
| Managed network create/delete | 1 minute | `NetworkConfiguration.Timeout` |
| Image build | 5 minutes | `ImageBuildConfiguration.Timeout` |

All deadlines must be positive finite timer durations. Cancellation tokens are propagated to async
operations; cancellation and an expired operation deadline throw `OperationCanceledException`.
Readiness strategy exhaustion throws `ContainerTimeoutException`. A smaller strategy timeout can
shorten, but cannot extend, the overall startup budget. A custom readiness predicate that ignores its
token cannot block the managed waiter indefinitely; its own work is not forcibly terminated.
Live log tails have no automatic deadline: cancel their enumeration token to stop following.

An unexpected one-shot exit fails immediately with `ContainerExitCodeException`, rather than
retrying readiness until startup expires. Docker exec cancellation stops waiting/reading on the
client; Docker does not provide an exec-process kill endpoint, so that process can continue inside
the container. WSLC exec cancellation signals the owned process. Neither means transactional rollback.

Failed startup runs bounded cleanup before rethrowing the original error, so the caller may observe
up to the cleanup budget in addition to the startup budget. A failed-start container is disposed;
build a new instance to retry.

**Native limitation:** synchronous WSLC calls (session start, create, signal, inspect, delete,
terminate) cannot be safely interrupted by a .NET token. Deadlines bound managed gate waits,
polling, image-pull waits and CLI processes, not a native call that has already entered the SDK.
The library deliberately does not abandon a native operation while releasing its session gate.
It does not promise a hard "zero hangs" guarantee against a blocked runtime or operating system.
CLI cancellation kills the owned command process tree, but does not roll back server-side work
already accepted by the runtime. Normal session cleanup remains the fallback.

## Networks

Resolve once and use that backend for the network and every attached container:

```csharp
var backend = await ContainerBackends.ResolveAsync(ct);
await using var network = await backend.CreateNetworkAsync(cancellationToken: ct);
await using var server = new ContainerBuilder(backend)
    .WithImage("redis:7")
    .WithNetwork(network.Name)
    .WithNetworkAliases("cache")
    .Build();
await server.StartAsync(ct);
```

Other containers on this network can use `cache:6379`; host clients still use published ports.
Dispose all attached containers before the network. The default WSLC bridge still has no
container-name DNS. `ConnectionMode.Container` is not automatically implemented by service-module
connection-string providers: use the explicit alias and service port when constructing such URLs.

## Image builds and files

```csharp
var backend = await ContainerBackends.ResolveAsync(ct);
await using var image = await backend.BuildImageAsync(new ImageBuildConfiguration
{
    ContextDirectory = Path.GetFullPath("test-image"),
    Name = $"my-test:{Guid.NewGuid():N}",
    Dockerfile = "Dockerfile",
    Timeout = TimeSpan.FromMinutes(5),
}, ct);
await using var container = new ContainerBuilder(backend)
    .WithImage(image.Name)
    .WithResourceMapping(Path.GetFullPath("fixture.json"), "/tmp/fixture.json")
    .WithCommand("sleep", "300")
    .Build();
await container.StartAsync(ct);
await container.CopyAsync(Path.GetFullPath("more-fixtures"), "/tmp/more", ct);
byte[] content = await container.ReadFileAsync("/tmp/fixture.json", ct);
```

The returned `IContainerResource` owns the built image. Dispose containers before disposing it;
deleting an image still used by a container can fail. The same ownership rule applies to networks.
Use unique names for independently owned resources.

## Reuse and platform limits

Reuse is opt-in, requires a stable explicit name, and cannot be combined with auto-remove.
`DisposeAsync` no longer promises deletion for a reusable container. Call `DeleteAsync(ct)` to
remove it deliberately.
Delete before disposing the wrapper; to remove a retained container later, reopen it with matching
reuse settings and delete through that wrapper.

Docker delegates reuse to Testcontainers: it survives wrapper/process lifetimes and opts out of
normal resource-reaper cleanup. WSLC reuse lasts only as long as its owning session, checks the
requested configuration fingerprint before reopening a named container, and is not cross-process
reuse. Do not depend on identical stop behavior during reusable-wrapper disposal between runtimes.
Do not share a reusable container with tests that mutate it concurrently.
The WSLC fingerprint checks configuration, not mapped-file contents or mutable image-tag contents.
Delete and recreate the container when those inputs change.

WSLC CLI-backed features require `wslc` on `PATH`. They fail with a prerequisite error if absent,
rather than changing the default SDK-only path. CLI creation cannot currently combine privileged
mode with CLI-only creation options and requires single-line environment values. Environment values
are passed through a temporary env file removed after creation, not rendered in command diagnostics.
Labels and names must not contain secrets. CLI errors deliberately omit potentially sensitive output.

UDP mapping and interactive stdin remain unsupported by the WSLC public contract; capture-based
exec rejects interactive stdin on both backends. Managed networks do not imply support for arbitrary
drivers, dynamic attachment APIs, or every Testcontainers network/build customization.
