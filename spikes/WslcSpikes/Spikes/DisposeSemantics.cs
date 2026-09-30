using System.Diagnostics;
using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S14: Does Session.Dispose() alone (no Terminate) free the session while the process is alive?
static class DisposeSemantics
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S14: Dispose-only semantics ===");
		string name = SpikeSupport.UniqueName("wslcspike-dispose");
		Session session = SpikeSupport.StartSession(name);
		Console.WriteLine("[s14] session running. wslc info:");
		await DumpSessionsAsync();

		Console.WriteLine("[s14] disposing WITHOUT terminate...");
		session.Dispose();
		await Task.Delay(2000);
		Console.WriteLine("[s14] after Dispose (process alive). wslc info:");
		await DumpSessionsAsync();

		Console.WriteLine("[s14] trying to reuse the name after Dispose-only...");
		try
		{
			Session reuse = SpikeSupport.StartSession(name);
			Console.WriteLine("[s14] name reuse AFTER Dispose-only: OK");
			SpikeSupport.Cleanup(reuse);
		}
		catch (Exception ex)
		{
			SpikeSupport.Dump(ex);
		}

		Console.WriteLine("[s14] S14 complete.");
		return 0;
	}

	private static async Task DumpSessionsAsync()
	{
		ProcessStartInfo psi = new ProcessStartInfo("wslc")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		psi.ArgumentList.Add("info");
		using System.Diagnostics.Process p =
			System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("failed to start wslc");
		string output = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
		await p.WaitForExitAsync().ConfigureAwait(false);
		Console.WriteLine(output);
	}
}
