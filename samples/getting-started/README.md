# Getting-started samples

Three runnable samples. `WslSample` and `DockerSample` differ in exactly one thing — which backend they
use — and the container code is identical, which is the point. `AutoSample` removes even that choice: it
references the umbrella (`Purview.Containers`) and lets automatic selection decide, so the same project
runs on WSL Containers on Windows and Docker everywhere else. See
[Backends: WSLC or Docker](../../docs/wiki/Backends.md) and
[Using it in your tests](../../docs/wiki/Using-in-Your-Tests.md).

| Sample | Backend | Target framework | Prerequisite |
| --- | --- | --- | --- |
| `WslSample` | `Purview.Containers.Wsl` | `net11.0-windows10.0.19041.0` | Windows with WSL Containers (`wsl --install --no-distribution`) |
| `DockerSample` | `Purview.Containers.Docker` | `net10.0` | a reachable Docker daemon (`docker info`) |
| `AutoSample` | umbrella (auto) | `net10.0` | either — WSLC on Windows, Docker elsewhere |

Run them from the repository root:

```powershell
just sample-auto       # or: dotnet run --project samples/getting-started/AutoSample
just sample-wsl        # or: dotnet run --project samples/getting-started/WslSample
just sample-docker     # or: dotnet run --project samples/getting-started/DockerSample
```

Each sample starts real containers, waits for them to be ready, prints the mapped host ports and the
services' output, and disposes them again.

## These are repository samples

They reference the backends as **projects** so they build from a clone with no package feed. A real
consumer uses packages instead:

```bash
dotnet add package Purview.Containers.Docker    # or Purview.Containers.Wsl
```

Because they are project references, no `buildTransitive` assets apply, so each sample registers its
backend explicitly (`ContainerBackends.Register(...)`); a package consumer gets that registration generated
automatically and never writes that line.
