# Getting-started samples

Two runnable samples that differ in exactly one thing: which backend they use. The container code is the
same, which is the point — see [Backends: WSLC or Docker](../../docs/wiki/Backends.md).

| Sample | Backend | Target framework | Prerequisite |
| --- | --- | --- | --- |
| `WslSample` | `Purview.Containers.Wsl` | `net11.0-windows10.0.19041.0` | Windows with WSL Containers (`wsl --install --no-distribution`) |
| `DockerSample` | `Purview.Containers.Docker` | `net10.0` | a reachable Docker daemon (`docker info`) |

Run them from the repository root:

```powershell
just sample-wsl        # or: dotnet run --project samples/getting-started/WslSample
just sample-docker     # or: dotnet run --project samples/getting-started/DockerSample
```

Each sample starts a real container, waits for it to log `ready`, prints the mapped host port and the
container's output, and disposes it again.

## These are repository samples

They reference the backends as **projects** so they build from a clone with no package feed. A real
consumer uses packages instead:

```bash
dotnet add package Purview.Containers.Docker    # or Purview.Containers.Wsl
```

Because they are project references, no `buildTransitive` assets apply, so each sample registers its
backend explicitly (`ContainerBackends.Register(...)`); a package consumer gets that registration generated
automatically and never writes that line.
