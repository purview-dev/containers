using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S1: Session lifecycle — start, terminate, warm re-open of the same name+storage path,
// and behaviour when a second session with the same name is created while the first is active.
static class SessionLifecycle
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S1: Session lifecycle ===");
		SpikeSupport.PrintRuntimeInfo();

		string name = "wslcspike-s1";
		string storage = Path.Combine(SpikeSupport.StorageRoot, name);
		if (Directory.Exists(storage))
		{
			Directory.Delete(storage, recursive: true);
		}

		Session first = SpikeSupport.StartSession(name, storage, memoryMb: 1024, cpuCount: 2);
		Console.WriteLine("[s1] session started (cold). Attempting a duplicate same-name session while active...");
		try
		{
			Session duplicate = new Session(new SessionSettings(name, storage));
			duplicate.Start();
			Console.WriteLine("[s1] duplicate same-name session started WITHOUT error (unexpected)");
			SpikeSupport.Cleanup(duplicate);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[s1] duplicate same-name session failed as expected:");
			SpikeSupport.Dump(ex);
		}

		SpikeSupport.Cleanup(first);
		Console.WriteLine("[s1] first session terminated+disposed");

		Console.WriteLine("[s1] re-opening SAME name+storage (warm restart)...");
		Session second = SpikeSupport.StartSession(name, storage, memoryMb: 1024, cpuCount: 2);
		SpikeSupport.Cleanup(second);
		Console.WriteLine("[s1] second session terminated+disposed");

		Console.WriteLine("[s1] re-opening SAME name with DIFFERENT storage path...");
		Session third = SpikeSupport.StartSession(name, Path.Combine(SpikeSupport.StorageRoot, name + "-alt"));
		SpikeSupport.Cleanup(third);
		Console.WriteLine("[s1] third session terminated+disposed");

		Console.WriteLine("[s1] S1 complete.");
		await Task.CompletedTask;
		return 0;
	}
}
