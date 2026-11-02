# Purview.Containers.Docker

The **Docker backend** for [`Purview.Containers.Core`](https://www.nuget.org/packages/Purview.Containers.Core):
throwaway containers for integration testing on any Docker daemon reachable from the test host — Docker
Desktop, Docker Engine inside WSL2, a remote daemon, Docker-in-Docker, or the daemon a CI runner
provides. It drives the daemon through [Testcontainers for .NET](https://dotnet.testcontainers.org/).

```bash
dotnet add package Purview.Containers.Docker
```

```csharp
await using var container = new ContainerBuilder()
    .WithImage("alpine:3.19")
    .WithCommand("/bin/sh", "-c", "echo ready && sleep 30")
    .WithWaitStrategy(Wait.ForLogMessage("ready"))
    .Build();

await container.StartAsync();
var result = await container.ExecAsync(["/bin/echo", "hello"]);
```

Referencing this package makes `docker` selectable; the module packages need no Docker-specific variant,
because a module reaches the backend through the shared abstractions:

```bash
PURVIEW_CONTAINERS_BACKEND=docker     # or "auto" (the default) to probe WSLC first, then Docker
```

| Rule | How |
| --- | --- |
| Pinned instance | `ContainerBackends.Use(new DockerContainerBackend())`, or `WithBackend(...)` |
| Named backend | `PURVIEW_CONTAINERS_BACKEND=docker` |
| Automatic detection | probe every registered backend; WSLC is preferred when it is usable |

## Requirements

- **.NET 10 or later**, any platform (this package is portable).
- A reachable Docker daemon. `DockerContainerBackend.GetInfoAsync()` pings it and reports the server
  version, or the reason it could not be reached, so a missing daemon is a clear message and not a
  timeout deep inside a test.
- The daemon must accept the standard Testcontainers configuration (`DOCKER_HOST`, Docker contexts,
  `~/.testcontainers.properties`). The resource reaper (Ryuk) is used, so a crashed test host does not
  leak containers.

## Behaviour notes

- Windows drive paths in bind mounts are translated automatically when the connected loopback TCP
  daemon is verified as a local WSL2 Docker Engine. The backend compares daemon IDs across running
  distributions, then invokes `wslpath` in the matching distribution, so custom drive mount roots work
  without consumer configuration. That distribution must expose its daemon through
  `/var/run/docker.sock` and allow its default user to run `docker info`. Docker Desktop, remote
  daemons, Linux paths and named volumes keep their existing behaviour.
- Readiness uses the same shared wait strategies as every other backend (host TCP/HTTP, an in-container
  command, or a log match), so module behaviour does not change with the backend.
- `ExecAsync` supports environment, working-directory and timeout overrides. Capture-based exec
  rejects interactive stdin. Cancellation/deadlines propagate as `OperationCanceledException`.
- Entrypoint, user, labels, pre-start/runtime file copy, one-shot exit waits, managed networks/aliases,
  Dockerfile builds and opt-in reuse are available. Defaults are five minutes for total startup,
  one minute for exec/copy/read/log snapshots, 15 seconds for stop and 30 seconds for cleanup.
  Reuse follows Testcontainers semantics and disables normal reaper cleanup; `DeleteAsync` removes
  retained containers explicitly. See
  [capabilities and ownership](https://github.com/purview-dev/containers/blob/main/docs/wiki/Backend-Capabilities.md).
- Log tailing polls the accumulated output until the container stops, because the Testcontainers API
  exposes no positioned log stream.

> **Experimental.** The public API, defaults and packaging rules can change between prereleases, and
> there is no production support guarantee. Pin the exact package version you build against.
