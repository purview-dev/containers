using Microsoft.WSL.Containers;
using Windows.Storage.Streams;

namespace WslcSpikes;

// S5: Secondary processes (exec) — stdout/stderr capture, exit codes, env, workdir, and stdin.
static class ExecProcess
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S5: exec + stdin ===");
		Session session = SpikeSupport.StartSession("wslcspike-s5");
		try
		{
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");
			Container container = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s5-host"),
				new[] { "/bin/sleep", "300" }
			);
			container.Start();
			Console.WriteLine("[s5] host container state={0}", container.State);

			Console.WriteLine("[s5] exec case 1: echo");
			Process exec1 = container.CreateProcess(
				new ProcessSettings
				{
					CommandLine = new List<string> { "/bin/echo", "hello-exec" },
					OutputMode = ProcessOutputMode.Event,
				}
			);
			ProcessCapture cap1 = SpikeSupport.Capture(exec1);
			exec1.Start();
			int code1 = await cap1.Exit.WaitAsync(TimeSpan.FromSeconds(20));
			Console.WriteLine("[s5] exec1 exit={0} stdout='{1}' stderr='{2}'", code1, cap1.Stdout, cap1.Stderr);
			exec1.Dispose();

			Console.WriteLine("[s5] exec case 2: env + workdir");
			Process exec2 = container.CreateProcess(
				new ProcessSettings
				{
					CommandLine = new List<string> { "/bin/sh", "-c", "echo $FOO; pwd" },
					EnvironmentVariables = new Dictionary<string, string> { ["FOO"] = "bar-value" },
					WorkingDirectory = "/tmp",
					OutputMode = ProcessOutputMode.Event,
				}
			);
			ProcessCapture cap2 = SpikeSupport.Capture(exec2);
			exec2.Start();
			int code2 = await cap2.Exit.WaitAsync(TimeSpan.FromSeconds(20));
			Console.WriteLine("[s5] exec2 exit={0} stdout='{1}' stderr='{2}'", code2, cap2.Stdout, cap2.Stderr);
			exec2.Dispose();

			Console.WriteLine("[s5] exec case 3: stdin via /bin/cat");
			Process exec3 = container.CreateProcess(
				new ProcessSettings
				{
					CommandLine = new List<string> { "/bin/cat" },
					EnableStandardInput = true,
					OutputMode = ProcessOutputMode.Event,
				}
			);
			ProcessCapture cap3 = SpikeSupport.Capture(exec3);
			exec3.Start();
			using (DataWriter writer = new DataWriter(exec3.GetInputStream()))
			{
				writer.WriteString("hello-stdin\n");
				await writer.StoreAsync();
				await writer.FlushAsync();
			}

			int code3 = await cap3.Exit.WaitAsync(TimeSpan.FromSeconds(20));
			Console.WriteLine("[s5] exec3 exit={0} stdout='{1}' stderr='{2}'", code3, cap3.Stdout, cap3.Stderr);
			exec3.Dispose();

			Console.WriteLine("[s5] exec case 4: failure exit code (exit 9)");
			Process exec4 = container.CreateProcess(
				new ProcessSettings
				{
					CommandLine = new List<string> { "/bin/sh", "-c", "echo boom >&2; exit 9" },
					OutputMode = ProcessOutputMode.Event,
				}
			);
			ProcessCapture cap4 = SpikeSupport.Capture(exec4);
			exec4.Start();
			int code4 = await cap4.Exit.WaitAsync(TimeSpan.FromSeconds(20));
			Console.WriteLine("[s5] exec4 exit={0} stdout='{1}' stderr='{2}'", code4, cap4.Stdout, cap4.Stderr);
			exec4.Dispose();

			container.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
			container.Delete(DeleteContainerOption.None);
			container.Dispose();
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s5] S5 complete.");
		return 0;
	}
}
