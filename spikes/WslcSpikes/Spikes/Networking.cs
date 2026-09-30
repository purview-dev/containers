using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S9: Container-to-container connectivity within one session.
// Server container A (hostname "alpha") runs httpd; client container B execs wget to http://alpha:8080/.
static class Networking
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S9: container-to-container networking ===");
		Session session = SpikeSupport.StartSession("wslcspike-s9");
		try
		{
			await SpikeSupport.PullImageAsync(session, HttpSupport.ServerImage);
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");
			string www = Path.Combine(SpikeSupport.StorageRoot, "s9-www");
			if (Directory.Exists(www))
			{
				Directory.Delete(www, recursive: true);
			}

			Directory.CreateDirectory(www);
			File.WriteAllText(Path.Combine(www, "index.html"), "hello-net\n");

			ContainerNetworkingMode?[] modes = new ContainerNetworkingMode?[]
			{
				null,
				ContainerNetworkingMode.Bridged,
				ContainerNetworkingMode.None,
			};
			foreach (ContainerNetworkingMode? mode in modes)
			{
				string label = mode?.ToString() ?? "Default";
				Console.WriteLine("[s9] --- mode: {0} ---", label);
				string suffix = (mode?.ToString() ?? "default").ToLowerInvariant();

				Container server = SpikeSupport.CreateContainer(
					session,
					HttpSupport.ServerImage,
					SpikeSupport.UniqueName($"s9-server-{suffix}"),
					HttpSupport.ServerArgs(8080),
					configure: settings =>
					{
						settings.HostName = "alpha";
						settings.NetworkingMode = mode;
						settings.Volumes = new List<ContainerVolume> { new ContainerVolume(www, "/tmp", false) };
					}
				);
				Container client = SpikeSupport.CreateContainer(
					session,
					"docker.io/library/alpine:latest",
					SpikeSupport.UniqueName($"s9-client-{suffix}"),
					new[] { "/bin/sleep", "300" },
					configure: settings => settings.NetworkingMode = mode
				);

				Console.WriteLine("[s9]   server hostname='alpha' networking={0}", label);
				server.Start();
				client.Start();
				await Task.Delay(1500);

				Console.WriteLine("[s9]   client tooling check:");
				await ExecInAsync(client, "command -v wget nc busybox");
				string? serverIp = HttpSupport.TryReadContainerIp(server.Inspect());
				Console.WriteLine("[s9]   server bridge IP = {0}", serverIp ?? "<none>");
				Console.WriteLine("[s9]   server /etc/hosts:");
				await ExecInAsync(client, "cat /etc/hosts");

				string[] probes = new[] { $"http://alpha:8080/", $"http://{serverIp ?? "??"}:8080/" };
				foreach (string probe in probes)
				{
					Console.WriteLine("[s9]   exec in client: wget {0}", probe);
					await ExecInAsync(client, $"wget -q -T 3 -O - {probe} 2>&1; echo wget_exit=$?");
				}

				server.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
				server.Delete(DeleteContainerOption.None);
				server.Dispose();
				client.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
				client.Delete(DeleteContainerOption.None);
				client.Dispose();
			}

			Directory.Delete(www, recursive: true);
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s9] S9 complete.");
		return 0;
	}

	private static async Task ExecInAsync(Container container, string command)
	{
		Process exec = container.CreateProcess(
			new ProcessSettings
			{
				CommandLine = new List<string> { "/bin/sh", "-c", command },
				OutputMode = ProcessOutputMode.Event,
			}
		);
		ProcessCapture cap = SpikeSupport.Capture(exec);
		exec.Start();
		try
		{
			int code = await cap.Exit.WaitAsync(TimeSpan.FromSeconds(15));
			Console.WriteLine("[s9]   exit={0} out='{1}' err='{2}'", code, cap.Stdout, cap.Stderr);
		}
		catch (TimeoutException)
		{
			Console.WriteLine("[s9]   timed out; killing exec");
			exec.Signal(Signal.SIGKILL);
		}

		exec.Dispose();
	}
}
