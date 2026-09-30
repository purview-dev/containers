// WSL Containers sample: the same code as the Docker sample, running on the WSLC backend.
// Prerequisite: `wsl --install --no-distribution`, then verify with `wsl --version` and `wslc version`.
using Purview.Containers;
using Purview.Containers.Waiting;
using Purview.Containers.Wsl;

// A package consumer gets backend registration generated from the package's buildTransitive assets, so
// this line does not appear in consumer code. The sample references the backend as a project, so it
// registers explicitly.
ContainerBackends.Register(new WslContainerBackend());

foreach (var backend in await ContainerBackends.ProbeAllAsync())
{
	Console.WriteLine($"backend   : {backend.Name} usable={backend.IsUsable} version={backend.Version}");
}

await using var container = new ContainerBuilder()
	.WithImage("alpine:3.19")
	.WithCommand("/bin/sh", "-c", "echo ready && sleep 30")
	.WithPortBinding(8080, assignRandomHostPort: true)
	.WithWaitStrategy(Wait.ForLogMessage("ready"))
	.Build();

await container.StartAsync();

Console.WriteLine($"container : {container.Name} ({container.Id[..12]})");
Console.WriteLine($"host port : {container.GetMappedPublicPort(8080)} -> 8080");
Console.WriteLine($"logs      : {(await container.GetLogsAsync()).Trim()}");
