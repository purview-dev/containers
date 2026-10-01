// The zero-configuration sample: the same test-style code a consumer writes, with no backend choice in
// code or environment. On a Windows machine with WSL Containers this runs on WSLC; on a machine with only
// Docker it runs on Docker. Prerequisite: either backend reachable (see docs/wiki/Using-in-Your-Tests.md).
using Purview.Containers;
using Purview.Containers.Docker;
using Purview.Containers.PostgreSql;
using Purview.Containers.Redis;
using Purview.Containers.Runtime;
using Purview.Containers.Wsl;

// A package consumer gets backend registration generated from the packages' buildTransitive assets, so
// this line does not appear in consumer code. The sample references the backends as projects, so it
// registers both explicitly - exactly what the generated initializer does - and lets automatic selection
// choose between them.
ContainerBackends.Register(WslContainerBackend.Create());
ContainerBackends.Register(DockerContainerBackend.Create());

Console.WriteLine("Registered backends (auto picks the first usable one):");
foreach (var backend in await ContainerBackends.ProbeAllAsync())
{
	Console.WriteLine($"  {backend.Name, -7} usable={backend.IsUsable} version={backend.Version}");
}

IContainerBackend selected;
try
{
	selected = await ContainerBackends.ResolveAsync();
}
catch (ContainerBackendUnavailableException exception)
{
	Console.WriteLine();
	Console.WriteLine("No usable backend on this machine - start Docker or install WSL Containers.");
	Console.WriteLine(exception.Message);
	return 0;
}

Console.WriteLine($"Selected backend: {selected.Name}");
Console.WriteLine();

await using var redis = new RedisBuilder().Build();
await redis.StartAsync();
var ping = await redis.ExecAsync(["redis-cli", "ping"]);
Console.WriteLine($"redis      : {redis.GetConnectionString()} -> {ping.Stdout.Trim()}");

await using var postgres = new PostgreSqlBuilder().WithDatabase("app").Build();
await postgres.StartAsync();
var ready = await postgres.ExecAsync(["pg_isready"]);
Console.WriteLine($"postgresql : {postgres.GetConnectionString()} -> {ready.Stdout.Trim()}");

return 0;
