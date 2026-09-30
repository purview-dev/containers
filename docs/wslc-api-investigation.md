# WSLC API investigation

Findings from Phase 0 investigation of **`Microsoft.WSL.Containers` v3.0.1** (NuGet) against **WSLC 3.0.1.0** (`wslc` CLI), on Windows 10.0.28020 / WSL 3.0.1.0 / kernel 6.18.40.1-1.

Every statement is tagged:

| Tag | Meaning |
|---|---|
| `DOC` | Official docs (learn.microsoft.com, wsl.dev/api-reference) |
| `SRC` | Verified against `wslcsdkcs.dll` metadata (dotnet-inspect) |
| `EXP` | Experimentally verified by `spikes/WslcSpikes` on this machine |
| `TEST` | Assumed; needs a spike before relying on it |

## Package shape

- Package: `Microsoft.WSL.Containers` v3.0.1, TFM `net8.0-windows10.0.19041.0`. `SRC`
- Ships: `lib/net8.0-windows10.0.19041.0/wslcsdkcs.dll` (C#/WinRT projection), `runtimes/win-x64/native/wslcsdk.dll`, `runtimes/win-arm64/native/wslcsdk.dll`, `winmd/Microsoft.WSL.Containers.winmd`, build targets. `SRC`
- Consuming projects must set `PlatformTarget=x64` (or `arm64`) or a matching `RuntimeIdentifier`; otherwise the native `wslcsdk.dll` is not copied. The build target errors if neither is set. `SRC`, `EXP`
- The projection references `Microsoft.Windows.SDK.NET` **10.0.26100.79**. Targeting `net*-windows10.0.19041.0` pulls `Microsoft.Windows.SDK.NET.Ref 10.0.19041.38` by default, which is **lower** and fails with `CS1705`. Fix: set `<WindowsSdkPackageVersion>10.0.26100.80</WindowsSdkPackageVersion>` (`.79` is not published). `SRC`, `EXP`

## Namespace

`using Microsoft.WSL.Containers;` `SRC`, `DOC`

## Types (21 classes, 1 struct, 14 enums, 4 delegates)

Key surface (SRC):

```
WslcService (static, no public ctor)
  static ServiceVersion GetVersion()
  static IReadOnlyList<Component> GetMissingComponents()
  static void InstallWithDependencies(InstallOptions)
  static IAsyncActionWithProgress<InstallProgress> InstallWithDependenciesAsync(InstallOptions)
  // NOTE: there is NO GetInfoAsync in 3.0.1

Session : IDisposable
  ctor(SessionSettings)
  void Start(); void Terminate(); void Dispose();
  Container CreateContainer(ContainerSettings)
  Container OpenContainer(string nameOrId, ProcessOutputMode)
  void PullImage(PullImageOptions); IAsyncActionWithProgress<ImageProgress> PullImageAsync(PullImageOptions)
  void ImportImage(path, imageName); ImportImageAsync(path, imageName)
  void LoadImage(path); LoadImageAsync(path)
  void PushImage(PushImageOptions); PushImageAsync(PushImageOptions)
  void DeleteImage(string nameOrId); void TagImage(TagImageOptions)
  IReadOnlyList<ImageInfo> GetImages()
  void CreateVhdVolume(VhdOptions); void DeleteVhdVolume(string name)
  AuthenticateResult Authenticate(Uri serverAddress, string username, string password)
  event SessionTerminationHandler Terminated; event ProcessCrashHandler ProcessCrashed

Container : IDisposable
  string Id; ContainerState State; Process InitProcess
  void Start(); void Stop(Signal, TimeSpan); void Delete(DeleteContainerOption)
  Process CreateProcess(ProcessSettings); string Inspect()

Process : IDisposable
  uint Pid; ProcessState State; int ExitCode
  void Start(); void Signal(Signal)
  IOutputStream GetInputStream()            // requires ProcessSettings.EnableStandardInput
  IInputStream GetOutputStream(ProcessOutputHandle)  // requires OutputMode.Stream
  event ProcessOutputHandler OutputReceived (byte[]); ErrorReceived (byte[]); event ProcessExitHandler Exited (int)
```

Settings / options (SRC):

- `SessionSettings(name, storagePath)` + `CpuCount?`, `MemorySizeInMB?`, `EnableGpu`, `Timeout?`, `VhdRequirements`, `Name`, `StoragePath`
- `ContainerSettings(imageName)` + `Name`, `HostName`, `DomainName`, `InitProcess`, `PortMappings(IList<ContainerPortMapping>)`, `Volumes(IList<ContainerVolume>)`, `NamedVolumes(IList<ContainerNamedVolume>)`, `NetworkingMode(ContainerNetworkingMode?)`, `Privileged`, `EnableGpu`, `EnableAutoRemove`
- `ProcessSettings()` + `CommandLine(IList<string>)`, `EnvironmentVariables(IDictionary<string,string>)`, `WorkingDirectory`, `OutputMode`, `EnableStandardInput`
- `ContainerPortMapping(ushort windowsPort, ushort containerPort, PortProtocol)` + `WindowsAddress(HostName)`
- `ContainerVolume(windowsPath, containerPath, readOnly)`
- `ContainerNamedVolume(name, containerPath, readOnly)`
- `PullImageOptions(uri)` + `RegistryAuth`; `PushImageOptions(image, registryAuth)`; `TagImageOptions(image, repository, tag)`
- `ImageInfo{Name, Sha256(IBuffer), Size, CreatedTimestamp}`
- `ImageProgress{Id, Status(ImageProgressStatus), CurrentBytes, TotalBytes}`
- `VhdOptions(name, size, type)` + `Owner(VhdOwner{Uid,Gid})`

Enums (SRC): `ContainerState{Invalid=0,Created,Running,Exited,Deleted}` · `ProcessState{Unknown,Running,Exited,Signalled}` · `ProcessOutputMode{Discard=0,Stream,Event}` · `ProcessOutputHandle{StandardOutput=1,StandardError=2}` · `Signal{None,SIGHUP=1,SIGINT,SIGQUIT,SIGKILL=9,SIGTERM=15}` · `DeleteContainerOption{None=0,Force}` · `PortProtocol{TCP=0,UDP}` · **`ContainerNetworkingMode{None=0,Bridged=1}`** · `Component{VirtualMachinePlatform=1,WslPlatform=2,SdkNeedsUpdate=4}` · `IdentityTokenType{Unknown,Token,Credentials}` · `SessionTerminationReason{Unknown,Shutdown,Crashed}` · `ImageProgressStatus{Unknown,Pulling,Waiting,Downloading,Verifying,Extracting,Complete}` · `VhdType{Dynamic=0,Fixed}` · `Error{...negative HRESULTs, see below}`

## Output modes (DOC, EXP)

- `OutputReceived` / `ErrorReceived` require `ProcessOutputMode.Event`.
- `GetOutputStream(handle)` requires `ProcessOutputMode.Stream`.
- `Exited` fires in all modes.
- Output mode is a single value per process — capture and stream are mutually exclusive.
- Callbacks deliver `byte[]` chunks, **not lines**. (EXP: echo produced one chunk; multi-chunk/UTF-8-split behaviour not stress-tested, but the API contract is chunked.)

## Container.Start() semantics (DOC, EXP)

- `Container.Start()` starts the init process. If `OutputMode` is `Event`/`Stream`, it auto-attaches. There is **no flags parameter**.
- `Process.Start()` is only for secondary processes (`Container.CreateProcess`); the init process is started by `Container.Start()`.
- If the init command does not exist, `Container.Start()` throws `ArgumentException` (`HResult 0x80070057`) whose message contains the OCI runtime error (`runc create failed: ... exec: "...": no such file or directory`). `EXP`
- `InitProcess` is only available when `ContainerSettings.InitProcess` was configured.

## Lifecycle & error semantics (EXP)

- Session start is fast (~20-30 ms) because the WSL VM is already running under the session manager.
- **Session names are machine-unique.** Creating a second session with an active name throws `COMException 0x800700B7` ("Cannot create a file when that file already exists"). `EXP`
- **A terminated/disposed session name is immediately reusable.** `EXP`
- `Session.Dispose()` alone removes the session from the session manager and frees the name while the process is still alive. `EXP (S14)`
- **If a process exits without disposing its session, the session is orphaned** — it remains in `wslc info` with a dead `Creator PID` and the name stays reserved. It does **not** block other session names, nor reuse of the same storage path. `EXP (S10, S13)`
- `Container.Stop(signal, timeout)` is **idempotent** (stopping an already-stopped container succeeds). `EXP`
- `Container.Delete(...)` on an already-deleted container throws `COMException 0x80010108` (`RPC_E_DISCONNECTED`). `EXP`
- `OpenContainer` on a missing container throws `COMException` with `HResult == (int)Error.ContainerNotFound` (`0x80040603`, i.e. `-2147219965`). `EXP` — matches docs.
- `Delete(DeleteContainerOption.Force)` works on a running container (state → `Deleted`). `EXP`
- `OpenContainer(nameOrId, ProcessOutputMode)` can reopen a running container and operate on the reopened handle. `EXP`

`Error` enum values (SRC): `ImageNotFound=-2147219967, ContainerPrefixAmbiguous=-2147219966, ContainerNotFound=-2147219965, VolumeNotFound=-2147219964, ContainerNotRunning=-2147219963, ContainerIsRunning=-2147219962, SessionReserved=-2147219961, InvalidSessionName=-2147219960, NetworkNotFound=-2147219959, WindowsUpdateSearchFailed=-2147219958, SdkUpdateNeeded=-2147219957, ContainerDisabled=-2147219956, RegistryBlockedByPolicy=-2147219955, VolumeNotAvailable=-2147219954, ContainerDeleted=-2147219949`. Exceptions surface as `COMException`/`ArgumentException` with `HResult` matching these values.

## Image management (EXP)

- `PullImageAsync` returns an awaitable `IAsyncActionWithProgress<ImageProgress>`; set `Progress` before awaiting.
- **Image store is keyed by the session storage path** (the storage VHD), not by session instance or session name:
  - Pull in session A on path P → a new session B with a different name on the SAME path P sees the image.
  - A session C on a DIFFERENT path sees an empty store.
  `EXP (S2)`
- Images therefore persist across session restarts and even across process crashes **as long as the storage path is reused**. Warm image cache = reuse a stable storage path.
- Concurrent `PullImageAsync` calls for the same image all succeed but are **not deduplicated** (each performs its own pull). The library must dedupe. `EXP (S11)`
- alpine pull ~2-3 s cold, python:3-alpine ~2-4 s cold, alpine ~0.7 s warm (already in store, still contacts registry). `EXP`

## Port mappings (EXP)

- `ContainerPortMapping(hostPort, containerPort, protocol)`.
- **`NetworkingMode` MUST be `Bridged` when port mappings are used.** With the default (`null`, which maps to Docker `NetworkMode: "none"`), `CreateContainer` throws `ArgumentException 0x80070057`. `EXP`
- **`windowsPort = 0` requests a random host port natively.** The assigned port appears in `Inspect()` → `Ports."8080/tcp"[0].HostPort`. This is race-free by construction. `EXP`
- Default bind address is **IPv4 loopback only** (`HostIp: "127.0.0.1"`); IPv6 `::1` does not connect. `EXP (S12)`
- Setting `WindowsAddress = new HostName("127.0.0.1")` binds explicitly; other addresses untested.
- UDP port mappings are **not implemented** (docs: `E_NOTIMPL`).
- Reusing the same host port across two containers in one session → `0x80072740` ("Only one usage of each socket address...") at `Start`. Host ports are exclusive per host. `EXP`

## Inspect() schema (EXP)

`Container.Inspect()` returns **Docker-compatible JSON**:
- `State.Status`/`State.Running`/`State.ExitCode`
- `HostConfig.NetworkMode` (`none` | `bridge`)
- `NetworkSettings.Networks.bridge.IPAddress` (e.g. `172.17.0.2`), `Gateway` (`172.17.0.1`)
- `Ports."<containerPort>/tcp"[].HostIp/HostPort`
- `Mounts[]` (`Type`, `Source`, `Destination`, `ReadWrite`)
- `Config.Cmd`, `Config.Entrypoint`, `Config.Env`, `Config.Image`, `Config.Labels`, `Config.WorkingDir`

The library parses this for `GetMappedPublicPort` and container IP.

## Networking (EXP)

- Default `NetworkingMode` (null) → `NetworkMode "none"`: the container has **no network** (no IP).
- `ContainerNetworkingMode.None` → same, no network.
- `ContainerNetworkingMode.Bridged` → container gets a `172.17.0.x` address; host→container via mapped ports works.
- **Container-to-container: reachable by IP but NOT by name.** `wget http://alpha:8080/` and `wget http://<container-name>:8080/` both fail with "bad address"; `/etc/hosts` contains only localhost plus the container's own `IP <short-id>`. There is no Docker-style DNS/alias. `EXP (S9)`
- The managed API has **no network objects** (no `network create/connect`), unlike the CLI.

## Processes / exec (EXP)

- Secondary processes via `Container.CreateProcess(ProcessSettings)` then `process.Start()`.
- Env vars (`EnvironmentVariables`), `WorkingDirectory`, and exit codes all work.
- stdin works when `EnableStandardInput = true`, writing via `Windows.Storage.Streams.DataWriter` over `process.GetInputStream()`; closing the writer closes stdin. `EXP`
- stderr is distinct from stdout in `Event` mode.
- Avoid shell: `CommandLine` is passed as argv directly.

## Mounts & volumes (EXP)

- `ContainerVolume(windowsPath, containerPath, readOnly)` = Windows bind mount; read-only is enforced (`touch` fails, "Read-only file system").
- `ContainerNamedVolume(name, containerPath, readOnly)` = session VHD volume.
- **Named volumes are auto-provisioned on first reference** — `CreateVhdVolume` is not required before using a named volume. A second container mounting the same named volume sees the first container's data (shared). `EXP`
- `CreateVhdVolume` fails with `0x800700B7` if the volume already exists (e.g. auto-created). Use `CreateVhdVolume` to pre-provision (with explicit size/owner) before any container references the name.
- `DeleteVhdVolume(name)` works after the volume's containers are removed.

### Image VOLUMEs are auto-provisioned on ext4 (EXP, Phase 6)

- WSLC auto-mounts image-declared `VOLUME` paths onto an **ext4 device** (e.g. `/dev/sdc`) with the image's intended ownership (RabbitMQ's `/var/lib/rabbitmq` is `rabbitmq:rabbitmq`, mode 1777).
- Bind-mounting a Windows directory onto such a path **replaces** it with **drvfs** (`virtiofs`), which ignores Unix `chown`/`chmod`. Permission-sensitive images (e.g. RabbitMQ's `.erlang.cookie`) then fail. Do not bind-mount over image volumes unless intended.
- `Microsoft.Data.SqlClient` hangs on the IPv6 `::1` address that `localhost` resolves to (WSLC maps IPv4 loopback only, EXP S12); use `127.0.0.1` explicitly for SQL Server.

### Concurrent exec (secondary) processes are safe; short-lived init processes are not (EXP, S19)

- Starting many exec processes concurrently across containers in one session works fine (S19: 6 containers × 5 concurrent execs, 0 failures).
- But a shared session is **slow under parallel load**: WSLC calls are serialized, so every container test in the loaded suite takes 100-170s instead of ~10s. A container whose init process is short-lived (`/bin/sleep 60` or less) **exits before a delayed `exec`/`stop` runs**, and `Process.Start()` then throws `COMException` "Container '...' is not running".
- Lesson for parallel integration tests: use a long-lived init process (`/bin/sleep 300`) whenever the test later execs into, or asserts the Running state of, the container.

### A session exclusively locks its storage VHD (EXP, S18)

- Each session's image store lives in `{storagePath}\storage.vhdx`. A running session locks that VHD; a **concurrent** session on the same path fails reads with `0x80070020` (`ERROR_SHARING_VIOLATION`, "file is being used by another process").
- **Sequential** reuse works: after a session ends, a new session on the same path sees the previous session's images (EXP S2/S13). There is no global/shared image cache in WSLC, so sharing images across sessions requires a shared storage path — safe only when sessions do not overlap. The library defaults to a shared store and falls back to an isolated per-process store on contention.

## Known gaps in the C# projection (DOC)

- Raw-handle-based `ImportImage`/`LoadImage` overloads not projected (file-path variants are).
- Raw native handles (`WslcGetSessionTerminationEvent`, `WslcGetProcessExitEvent`, `WslcGetProcessIOHandle`) wrapped as events/streams.
- `WslcContainerStartFlags` not exposed; `Start()` auto-attaches for `Event`/`Stream`.
- UDP port mappings and fixed VHDs return `E_NOTIMPL`.
- `WslcService` has no `GetInfoAsync`; runtime info = `GetVersion()` + `GetMissingComponents()`.

## Documentation vs implementation discrepancies (recorded)

| Docs (overview page) | Actual 3.0.1 assembly |
|---|---|
| `ComponentFlags` | `Component` (enum, `IReadOnlyList<Component>`) |
| `MemoryMB` | `MemorySizeInMB` |
| `CmdLine` | `CommandLine` |
| `DeleteContainerFlags` | `DeleteContainerOption` |

The C# core-class reference pages (wsl.dev) match the assembly. Prefer the assembly.