using System.Diagnostics;
using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S10: What happens to a session when its creator process exits without terminating it?
// This harness spawns itself as a child (s10child), the child leaves a session running,
// and then we inspect `wslc info` to see whether the session was reaped.
static class OrphanSessions
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S10: orphaned session on creator exit ===");
		await DumpSessionsAsync("baseline");

		await RunChildAsync("s10child", "wslcspike-orphan-" + Guid.NewGuid().ToString("N")[..6], dispose: true);
		await DumpSessionsAsync("after dispose+exit child + 5s");

		await RunChildAsync(
			"s10child-norelease",
			"wslcspike-orphan-" + Guid.NewGuid().ToString("N")[..6],
			dispose: false
		);
		await DumpSessionsAsync("after no-release exit child + 5s");

		Console.WriteLine("[s10] S10 complete.");
		return 0;
	}

	private static async Task RunChildAsync(string mode, string name, bool dispose)
	{
		Console.WriteLine("[s10] spawning child '{0}' with session name '{1}'", mode, name);
		string exe = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
		ProcessStartInfo psi = new ProcessStartInfo(exe)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		psi.ArgumentList.Add(mode);
		psi.ArgumentList.Add(name);

		using System.Diagnostics.Process child =
			System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("failed to start child");
		string childOut = await child.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
		string childErr = await child.StandardError.ReadToEndAsync().ConfigureAwait(false);
		await child.WaitForExitAsync().ConfigureAwait(false);
		Console.WriteLine("[s10] child exit code={0}", child.ExitCode);
		Console.WriteLine("[s10] child stdout:\n{0}", childOut);
		Console.WriteLine("[s10] child stderr:\n{0}", childErr);

		Console.WriteLine("[s10] waiting 5s for the session manager to notice...");
		await Task.Delay(5000).ConfigureAwait(false);
	}

	public static async Task<int> ChildAsync(string name, bool dispose)
	{
		Session session = SpikeSupport.StartSession(name);
		Console.WriteLine("[s10child] started, sleeping 15s, then exiting with dispose={0}", dispose);
		await Task.Delay(15000).ConfigureAwait(false);
		if (dispose)
		{
			session.Dispose();
		}

		Console.WriteLine("[s10child] exiting");
		return 0;
	}

	private static async Task DumpSessionsAsync(string label)
	{
		Console.WriteLine("[s10] wslc info ({0}):", label);
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
		string error = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
		await p.WaitForExitAsync().ConfigureAwait(false);
		Console.WriteLine(output);
		if (!string.IsNullOrEmpty(error))
		{
			Console.WriteLine("[s10] wslc stderr: {0}", error);
		}
	}
}
