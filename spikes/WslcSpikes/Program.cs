using WslcSpikes;

// Phase 0 spike harness. Each subcommand proves one WSLC behaviour.
// Usage: dotnet run --project spikes/WslcSpikes -- <spike> [args]

if (args.Length == 0)
{
	Console.Error.WriteLine(
		"No spike specified. Available: s1 s2 s3 s4 s5 s6 s7 s8 s9 s10 s10child s10child-norelease s11 s12 s13 s14 s18 s19 sfull"
	);
	return 1;
}

return args[0] switch
{
	"s1" => await SessionLifecycle.RunAsync(),
	"s2" => await ImageCacheScope.RunAsync(),
	"s3" => await ProcessIo.RunAsync(),
	"s4" => await Ports.RunAsync(),
	"s5" => await ExecProcess.RunAsync(),
	"s6" => await MountsAndVolumes.RunAsync(),
	"s7" => await CleanupBehaviour.RunAsync(),
	"s8" => await ConcurrentContainers.RunAsync(),
	"s9" => await Networking.RunAsync(),
	"s10" => await OrphanSessions.RunAsync(),
	"s10child" => await OrphanSessions.ChildAsync(args.Length > 1 ? args[1] : "wslcspike-orphan-child", dispose: true),
	"s10child-norelease" => await OrphanSessions.ChildAsync(
		args.Length > 1 ? args[1] : "wslcspike-orphan-child",
		dispose: false
	),
	"s11" => await ThreadSafety.RunAsync(),
	"s12" => await BindAddress.RunAsync(),
	"s13" => await ReopenOrphan.RunAsync(args),
	"s14" => await DisposeSemantics.RunAsync(),
	"s18" => await ConcurrentSharedPath.RunAsync(),
	"s19" => await ConcurrentExecProcesses.RunAsync(),
	"sfull" => await FullLifecycle.RunAsync(),
	_ => UnknownSpike(args[0]),
};

static int UnknownSpike(string spike)
{
	Console.Error.WriteLine($"Spike '{spike}' is not implemented yet.");
	return 2;
}
