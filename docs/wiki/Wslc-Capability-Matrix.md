# WSLC capability matrix

Comparison of important Testcontainers-for-.NET capabilities against WSLC (`Microsoft.WSL.Containers` 3.0.1) and the planned implementation status in this library.

Legend: **Implemented** = planned in this library; **Mapped** = native WSLC API exists; **Gap** = no managed API (CLI-only or missing).

| Capability | Testcontainers | WSLC API | Implemented | Notes |
|---|---|---|---|---|
| Engine / session management | Docker daemon | `Session` (per-process VM host) | ✅ | session-scoped; image store keyed by storage path |
| Pull image + progress | ✅ | `PullImageAsync` | ✅ | `IAsyncActionWithProgress<ImageProgress>` |
| Ensure image exists | ✅ | `GetImages()` + pull | ✅ | per-path store; `PullPolicy.Missing/Always/Never` |
| List / delete / tag images | ✅ | `GetImages` / `DeleteImage` / `TagImage` | ✅ | |
| Import / load image | ✅ | `ImportImage*` / `LoadImage*` | ⚠️ | file-path based only |
| Registry authentication | ✅ | `Session.Authenticate` → `RegistryAuth` token | ✅ | `RegistryCredentials` (Phase 7); secrets redacted |
| Container create / start / stop / delete | ✅ | `CreateContainer` / `Start` / `Stop` / `Delete` | ✅ | no restart; stop is idempotent |
| Environment variables | ✅ | `ProcessSettings.EnvironmentVariables` | ✅ | applies to init and exec processes |
| Working directory | ✅ | `ProcessSettings.WorkingDirectory` | ✅ | |
| Entrypoint vs command | ✅ separate fields | ❌ single `CommandLine` argv | ⚠️ | concatenate entrypoint+command; document |
| Hostname / domainname | ✅ | `HostName` / `DomainName` | ✅ | hostname is not resolvable between containers |
| Host port mapping | ✅ | `ContainerPortMapping` | ✅ | requires `NetworkingMode=Bridged` |
| Random host ports | ✅ `-p 0:80` | ✅ `windowsPort=0` | ✅ | **native, race-free**; read from `Inspect()` |
| UDP ports | ✅ | ❌ `E_NOTIMPL` | ❌ | reject at validation with clear error |
| Bind mounts | ✅ | `ContainerVolume` | ✅ | read-only supported |
| Named volumes | ✅ | `ContainerNamedVolume` + VHD | ✅ | auto-provisioned on first reference |
| Container-to-container networking | ✅ networks | ⚠️ by IP only (Bridged) | ⚠️ | no DNS/aliases; no network objects |
| Exec (run command in container) | ✅ | `Container.CreateProcess` + `Process.Start` | ✅ | argv passthrough, no shell by default |
| Exec stdout/stderr | ✅ | events (`Event`) / streams (`Stream`) | ✅ | |
| Exec stdin | ✅ | `GetInputStream()` (`EnableStandardInput`) | ✅ | |
| Init-process logs | ✅ `logs` | events/streams on `InitProcess` | ✅ | library buffers + tails |
| Health check (`HEALTHCHECK`) | ✅ inspect | ❌ managed API (CLI only) | ❌ | use wait strategies instead |
| Resource limits (cpus/mem per container) | ✅ | ❌ session-level only | ❌ | `SessionSettings.CpuCount/MemorySizeInMB` |
| Privileged mode | ✅ | `ContainerSettings.Privileged` | ✅ | |
| GPU access | ✅ | `ContainerSettings.EnableGpu` / session `EnableGpu` | ✅ | |
| Copy files into container | ✅ `cp` | ❌ managed API (CLI only) | ❌ | |
| Build images | ✅ | ❌ managed API (CLI/build targets only) | ❌ | `WslcImage` MSBuild items exist |
| Export/save images | ✅ | ❌ managed API (CLI only) | ❌ | |
| Container inspect | ✅ | `Container.Inspect()` (Docker JSON) | ✅ | used for ports/IP |
| Container stats | ✅ `stats` | ❌ managed API (CLI only) | ❌ | |
| Events | ✅ | ❌ managed API (CLI only) | ❌ | session `Terminated`/`ProcessCrashed` only |
| User (uid/gid) | ✅ | ❌ `ProcessSettings`/`ContainerSettings` | ❌ | CLI `--user` only |
| TTY | ✅ | ❌ | ❌ | no TTY in managed API |
| Labels | ✅ | ❌ `ContainerSettings` | ❌ | CLI `--label` only |
| DNS options | ✅ | ❌ `ContainerSettings` | ❌ | CLI `--dns*` only |
| tmpfs / shm-size / ulimit | ✅ | ❌ | ❌ | CLI only |
| Copy between containers | ✅ | ❌ | ❌ | |
| Auto-remove | ✅ `--rm` | `ContainerSettings.EnableAutoRemove` | ✅ | |
| Reuse (Ryuk `testcontainers.properties` reuse) | ✅ | n/a (warm storage-path reuse) | ✅ | image cache + warm sessions |
| Resource reaper | ✅ Ryuk sidecar | native session manager | ⚠️ | Dispose frees session; orphans need CLI sweep |

## Not implemented by WSLC managed API (do not fake)

1. UDP port mappings — `E_NOTIMPL`.
2. Fixed VHDs — `E_NOTIMPL` (dynamic only).
3. User-defined networks / network objects — CLI only.
4. Container health checks, stats, events, cp, export/save/build — CLI only.
5. Per-container CPU/memory limits — session-level only.
6. TTY, `--user`, labels, DNS options, tmpfs/shm/ulimit in `ContainerSettings`/`ProcessSettings`.

Where a Testcontainers feature has no WSLC equivalent, the library must either (a) provide the closest supported behaviour and document it, or (b) fail fast with a clear `ContainerNotSupportedException`.