using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S18: Can two sessions share one storage path? This determines whether the image store
// can be shared across sessions/processes by default.
//   1. Session A (name a) on shared path P pulls alpine.
//   2. Session B (name b) on the SAME path P is started while A is still active (concurrent).
//   3. Does B start? Does B see A's image? Does a pull in B become visible to A?
static class ConcurrentSharedPath
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S18: concurrent sessions sharing a storage path ===");
		string path = Path.Combine(SpikeSupport.StorageRoot, "shared-path-spike");
		if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive: true);
		}

		Session? a = null;
		Session? b = null;
		try
		{
			Console.WriteLine("[s18] starting session A on shared path...");
			a = SpikeSupport.StartSession("wslcspike-shared-a", path);
			await SpikeSupport.PullImageAsync(a, "docker.io/library/alpine:latest");
			SpikeSupport.PrintImages(a, "A after pulling alpine");

			Console.WriteLine("[s18] starting session B CONCURRENTLY on the same path...");
			try
			{
				b = SpikeSupport.StartSession("wslcspike-shared-b", path);
			}
			catch (Exception ex)
			{
				Console.WriteLine(
					"[s18] RESULT: session B FAILED to start on the shared path (concurrent sharing unsafe):"
				);
				SpikeSupport.Dump(ex);
				return 0;
			}

			SpikeSupport.PrintImages(b, "B (same path, concurrent) - does it see alpine?");
			Console.WriteLine("[s18] pulling busybox in B...");
			try
			{
				await SpikeSupport.PullImageAsync(b, "docker.io/library/busybox:latest");
			}
			catch (Exception ex)
			{
				Console.WriteLine("[s18] pull in B FAILED:");
				SpikeSupport.Dump(ex);
			}

			SpikeSupport.PrintImages(a, "A after B pulled busybox (shared store if busybox appears)");
			SpikeSupport.PrintImages(b, "B after pulling busybox");

			Console.WriteLine("[s18] starting containers in A and B concurrently...");
			try
			{
				Container ca = SpikeSupport.CreateContainer(
					a,
					"docker.io/library/alpine:latest",
					"s18-a",
					new[] { "/bin/echo", "from-a" }
				);
				Container cb = SpikeSupport.CreateContainer(
					b,
					"docker.io/library/alpine:latest",
					"s18-b",
					new[] { "/bin/echo", "from-b" }
				);
				await Task.WhenAll(Task.Run(() => ca.Start()), Task.Run(() => cb.Start()));
				await Task.Delay(1500);
				Console.WriteLine("[s18] A container state={0}, B container state={1}", ca.State, cb.State);
				ca.Delete(DeleteContainerOption.Force);
				cb.Delete(DeleteContainerOption.Force);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[s18] concurrent container start FAILED:");
				SpikeSupport.Dump(ex);
			}

			Console.WriteLine("[s18] RESULT: concurrent shared-path sessions appear to WORK.");
		}
		finally
		{
			if (b is not null)
			{
				SpikeSupport.Cleanup(b);
			}

			if (a is not null)
			{
				SpikeSupport.Cleanup(a);
			}
		}

		Console.WriteLine("[s18] S18 complete.");
		return 0;
	}
}
