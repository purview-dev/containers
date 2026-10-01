using Purview.Containers;
using Purview.Containers.Waiting;
using Purview.Containers.Wsl;

// Phase 1 acceptance probe: this project is a plain net10.0 consumer (no Windows target framework).
// Everything below is the documented, backend-neutral API - the "auto" story the feature exists for.

Console.WriteLine($"[env] tfm      : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"[env] isWindows: {OperatingSystem.IsWindows()}");
Console.WriteLine();

// A package consumer gets this from the generated module initializer; a project-reference consumer
// registers explicitly.
ContainerBackends.Register(WslContainerBackend.Create());

var info = await WslContainerRuntime.Instance.GetInfoAsync();
Console.WriteLine($"[host] wsl available={info.IsAvailable} compatible={info.IsCompatible} version={info.Version}");
foreach (var missing in info.MissingComponents)
{
	Console.WriteLine($"[host]   missing: {missing}");
}

foreach (var backend in await ContainerBackends.ProbeAllAsync())
{
	Console.WriteLine($"[probe] {backend.Name}: usable={backend.IsUsable} version={backend.Version}");
}

if (!info.IsAvailable || !info.IsCompatible)
{
	Console.WriteLine("[result] WSLC is not usable on this host; auto-selection would fall through to Docker.");
	return 0;
}

Console.WriteLine();

await using var container = new ContainerBuilder()
	.WithImage("alpine:3.19")
	.WithCommand("/bin/sh", "-c", "echo ready && sleep 20")
	.WithPortBinding(8080, assignRandomHostPort: true)
	.WithWaitStrategy(Wait.ForLogMessage("ready"))
	.Build();

await container.StartAsync();

Console.WriteLine($"[container] name : {container.Name}");
Console.WriteLine($"[container] id   : {container.Id[..12]}");
Console.WriteLine($"[container] port : {container.GetMappedPublicPort(8080)} -> 8080");
Console.WriteLine($"[container] logs : {(await container.GetLogsAsync()).Trim()}");
Console.WriteLine("[result] PASS - a net10.0 project ran a real WSLC container through the portable facade.");
return 0;
