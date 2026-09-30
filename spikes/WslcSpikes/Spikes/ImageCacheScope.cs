using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S2: Image-store scope.
//   A: pull alpine in session A (path PA).
//   B: new session name, SAME path PA -> does GetImages see alpine? (persists in storage VHD?)
//   C: new session name, DIFFERENT path PB -> does GetImages see alpine? (per-path store?)
static class ImageCacheScope
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S2: Image cache scope ===");

		string pathA = Path.Combine(SpikeSupport.StorageRoot, "s2a");
		string pathB = Path.Combine(SpikeSupport.StorageRoot, "s2b");
		foreach (string path in new[] { pathA, pathB })
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}

		Session a = SpikeSupport.StartSession("wslcspike-s2a", pathA);
		await SpikeSupport.PullImageAsync(a, "docker.io/library/alpine:latest");
		SpikeSupport.PrintImages(a, "session A after pull");
		SpikeSupport.Cleanup(a);
		Console.WriteLine("[s2] session A terminated");

		Session b = SpikeSupport.StartSession("wslcspike-s2b", pathA);
		SpikeSupport.PrintImages(b, "session B (same storage path, new session)");
		SpikeSupport.Cleanup(b);
		Console.WriteLine("[s2] session B terminated");

		Session c = SpikeSupport.StartSession("wslcspike-s2c", pathB);
		SpikeSupport.PrintImages(c, "session C (different storage path)");
		SpikeSupport.Cleanup(c);
		Console.WriteLine("[s2] session C terminated");

		Console.WriteLine("[s2] S2 complete.");
		return 0;
	}
}
