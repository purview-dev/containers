# Purview.Containers.Environment

> **Experimental.** Pin the exact prerelease version; the API and defaults may change.

Resolve client-side bind sources against a Docker-compatible daemon without assuming the daemon
runs on the client machine. This library has no Aspire or AspireC4 dependency.

```csharp
using Purview.Containers.Environment;

using var environment = new ContainerEnvironmentResolver(new ContainerEnvironmentOptions());
var source = await environment.ResolveBindSourceAsync(localDirectory, cancellationToken);
var mount = new ContainerBindMount(source, "/data", IsReadOnly: true);
// Pass mount.ToString() as the argument after --mount, using ProcessStartInfo.ArgumentList.
```

Keep the resolver for the duration of a run. Native paths are tried first. On Windows, if native
sharing fails, running WSL distributions supply candidates via `wslpath`. A candidate is accepted
only when a container reads a unique client-created marker and writes a marker the client can read.
Neither distribution names nor mount roots are assumed. No distribution is started automatically.
Files are resolved through their parent directory; target paths and named volumes are not translated.

## Configuration

The constructor accepts explicit options. `ContainerEnvironmentResolver.ForRuntime("docker")` instead caches
a resolver per command and snapshots these environment variables on first use:

| Variable | Meaning |
|---|---|
| `CONTAINER_ENVIRONMENT_PATH_MAPPINGS` | JSON object mapping client roots to daemon roots; the longest matching root wins |
| `CONTAINER_ENVIRONMENT_WSL_DISTRIBUTION` | Restrict fallback translation to this already-running distribution |
| `CONTAINER_ENVIRONMENT_PROBE_IMAGE` | Override the default `alpine:3.19` probe image, for example with an internal mirror |

Explicit mappings are authoritative and verified, never silently replaced by another mapping.
Root boundaries are respected: mapping `project` does not match `project-other`.

The CLI inherits Docker's existing environment, active context, SSH/TLS configuration and credentials.
The resolver never changes them. A custom `RuntimeCommand` also supports Docker-compatible CLIs such
as Podman. Individual commands are bounded by `CommandTimeout` (60 seconds by default); cancellation
terminates the client process and attempts to remove only the uniquely named probe container.

## Supported environments and limits

- Native Linux Docker and Docker Desktop sharing: verified native paths.
- Windows client with a Linux daemon inside WSL: verified `wslpath` translation.
- Remote daemons with an already-shared filesystem: explicit verified root mappings.
- Unshared remote filesystems: fail with a configuration diagnostic. There is deliberately no silent
  snapshot/copy fallback: it would make live file changes disappear.

The client needs write access to the source directory to create and remove verification markers,
even when the eventual application mount is read-only. Successful directory resolutions are cached
for this resolver's lifetime; create a new resolver after changing Docker connections or file sharing.
The probe image must run Linux `sh`, `cat` and `printf`. Offline hosts must preload it.

Daemon access, file sharing, and published-port connectivity are independent. This package resolves
file sharing only: it does not infer a service address from `DOCKER_HOST`, change endpoint URLs,
open firewalls, install forwarding rules, or expose an unauthenticated Docker API. Configure remote
service access in your orchestrator, and test it using an actual HTTP/database request.

`ResolveBindMountsAsync` adapts `--mount` arguments in a `ProcessStartInfo` you construct. Prefer the
typed `ContainerBindMount` formatter to preserve paths containing commas and spaces. It does not
parse shell commands or rewrite `-v` arguments. Pass arguments beginning with `run`, `create`,
`container run` or `container create`; image command arguments are not rewritten.

For Aspire applications, use the separate `Purview.Containers.Aspire` adapter.
Neither package brings the umbrella package, Testcontainers or the Microsoft WSL Containers backend.
Docker Engine hosted inside a WSL distribution is distinct from Microsoft WSL Containers (WSLC).
