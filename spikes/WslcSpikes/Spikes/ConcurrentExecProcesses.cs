using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S19: Do concurrent exec (secondary) processes fail with "container is not running"?
// The integration suite's ExecTests + CommandWaitStrategy fail deterministically in a full parallel
// run (but pass alone) with COMException "Container '...' is not running." from Process.Start().
// Reproduce that at the raw API level: several sleep-60 containers in one session, concurrent execs.
static class ConcurrentExecProcesses
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S19: concurrent exec processes ===");
		Session session = SpikeSupport.StartSession("wslcspike-s19");
		try
		{
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");

			int containerCount = 6;
			int execsPerContainer = 5;
			Console.WriteLine($"[s19] {containerCount} containers x {execsPerContainer} concurrent execs");

			Container[] containers = new Container[containerCount];
			for (int i = 0; i < containerCount; i++)
			{
				containers[i] = SpikeSupport.CreateContainer(
					session,
					"docker.io/library/alpine:latest",
					SpikeSupport.UniqueName($"s19-{i}"),
					new[] { "/bin/sleep", "60" }
				);
			}

			Console.WriteLine("[s19] starting containers concurrently...");
			await Task.WhenAll(containers.Select(c => Task.Run(() => c.Start())));
			await Task.Delay(500);
			for (int i = 0; i < containerCount; i++)
			{
				Console.WriteLine("[s19] container s19-{0} state={1} init={2}", i, containers[i].State, containers[i].InitProcess.State);
			}

			int failures = 0;
			int total = containerCount * execsPerContainer;
			DateTime start = DateTime.UtcNow;
			Task<string>[] execs = new Task<string>[total];
			int index = 0;
			for (int c = 0; c < containerCount; c++)
			{
				for (int e = 0; e < execsPerContainer; e++)
				{
					int ci = c;
					int ei = e;
					execs[index++] = Task.Run(async () =>
					{
						Process process = containers[ci].CreateProcess(
							new ProcessSettings
							{
								CommandLine = new List<string> { "/bin/echo", "hi" },
								OutputMode = ProcessOutputMode.Event,
							}
						);
						ProcessCapture capture = SpikeSupport.Capture(process);
						try
						{
							process.Start();
							int code = await capture.Exit.WaitAsync(TimeSpan.FromSeconds(10));
							return $"exec c{ci} e{ei}: ok exit={code}";
						}
						catch (Exception ex)
						{
							return $"exec c{ci} e{ei}: FAIL {ex.Message} (HResult=0x{ex.HResult:X8}) containerState={containers[ci].State} initState={containers[ci].InitProcess.State}";
						}
						finally
						{
							try
							{
								process.Dispose();
							}
							catch { }
						}
					});
				}
			}

			string[] results = await Task.WhenAll(execs);
			TimeSpan elapsed = DateTime.UtcNow - start;
			foreach (string result in results)
			{
				Console.WriteLine("[s19] {0}", result);
				if (result.Contains("FAIL"))
				{
					failures++;
				}
			}

			Console.WriteLine("[s19] failures={0}/{1} elapsed={2}", failures, total, elapsed);

			for (int i = 0; i < containerCount; i++)
			{
				containers[i].Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
				containers[i].Delete(DeleteContainerOption.None);
				containers[i].Dispose();
			}
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s19] S19 complete.");
		return 0;
	}
}