using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S13: Can a new process reuse the name of a session orphaned by a dead process?
// Usage: s13 <sessionName>   (default: wslcspike-orphan-a9a74c, the S10 orphan)
static class ReopenOrphan
{
	public static async Task<int> RunAsync(string[] args)
	{
		string name = args.Length > 1 ? args[1] : "wslcspike-orphan-a9a74c";
		Console.WriteLine("=== S13: reuse orphaned session name '{0}' ===", name);
		try
		{
			Session session = SpikeSupport.StartSession(name);
			Console.WriteLine("[s13] started WITHOUT error; adopting the orphan's storage.");
			SpikeSupport.PrintImages(session, "images visible after adopting orphan name");
			SpikeSupport.Cleanup(session);
			Console.WriteLine("[s13] cleaned up");
		}
		catch (Exception ex)
		{
			SpikeSupport.Dump(ex);
			Console.WriteLine(
				"[s13] same-name reuse failed (expected). Testing fresh name + same shared storage path while orphan is active..."
			);
			try
			{
				Session fresh = SpikeSupport.StartSession(SpikeSupport.UniqueName("wslcspike-fresh"));
				Console.WriteLine(
					"[s13] fresh name + same storage path started OK; orphan does NOT block the storage path"
				);
				SpikeSupport.Cleanup(fresh);
			}
			catch (Exception ex2)
			{
				SpikeSupport.Dump(ex2);
				Console.WriteLine("[s13] fresh name + same storage path FAILED; orphan blocks the storage path");
			}
		}

		await Task.CompletedTask;
		return 0;
	}
}
