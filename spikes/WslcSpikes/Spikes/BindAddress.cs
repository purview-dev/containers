using Microsoft.WSL.Containers;
using Windows.Networking;

namespace WslcSpikes;

// S12: Host bind address — default vs explicit WindowsAddress, IPv4 vs IPv6 localhost reachability.
static class BindAddress
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S12: host bind address ===");
		Session session = SpikeSupport.StartSession("wslcspike-s12");
		try
		{
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");
			string www = Path.Combine(SpikeSupport.StorageRoot, "s12-www");
			if (Directory.Exists(www))
			{
				Directory.Delete(www, recursive: true);
			}

			Directory.CreateDirectory(www);
			File.WriteAllText(Path.Combine(www, "index.html"), "hello-bind\n");

			Console.WriteLine("[s12] case 1: default WindowsAddress");
			Container c1 = SpikeSupport.CreateContainer(
				session,
				HttpSupport.ServerImage,
				SpikeSupport.UniqueName("s12-default"),
				HttpSupport.ServerArgs(8080),
				configure: settings =>
				{
					settings.NetworkingMode = ContainerNetworkingMode.Bridged;
					settings.Volumes = new List<ContainerVolume> { new ContainerVolume(www, "/tmp", false) };
					settings.PortMappings = new List<ContainerPortMapping>
					{
						new ContainerPortMapping(0, 8080, PortProtocol.TCP),
					};
				}
			);
			c1.Start();
			string inspect1 = c1.Inspect();
			Console.WriteLine("[s12] inspect (default):\n{0}", Indent(inspect1));
			int? hp1 = HttpSupport.TryReadAssignedHostPort(inspect1);
			if (hp1 is int p1)
			{
				bool v4 = await HttpSupport.WaitForTcpAsync(p1, TimeSpan.FromSeconds(30));
				bool v6 = await HttpSupport.WaitForTcp6Async(p1, TimeSpan.FromSeconds(10));
				Console.WriteLine("[s12] host port {0}: ipv4={1} ipv6={2}", p1, v4, v6);
			}

			Console.WriteLine("[s12] case 2: explicit WindowsAddress 127.0.0.1");
			Container c2 = SpikeSupport.CreateContainer(
				session,
				HttpSupport.ServerImage,
				SpikeSupport.UniqueName("s12-explicit"),
				HttpSupport.ServerArgs(8080),
				configure: settings =>
				{
					settings.NetworkingMode = ContainerNetworkingMode.Bridged;
					settings.Volumes = new List<ContainerVolume> { new ContainerVolume(www, "/tmp", false) };
					settings.PortMappings = new List<ContainerPortMapping>
					{
						new ContainerPortMapping(0, 8080, PortProtocol.TCP)
						{
							WindowsAddress = new HostName("127.0.0.1"),
						},
					};
				}
			);
			c2.Start();
			string inspect2 = c2.Inspect();
			Console.WriteLine("[s12] inspect (explicit 127.0.0.1):\n{0}", Indent(inspect2));
			int? hp2 = HttpSupport.TryReadAssignedHostPort(inspect2);
			if (hp2 is int p2)
			{
				bool v4 = await HttpSupport.WaitForTcpAsync(p2, TimeSpan.FromSeconds(30));
				bool v6 = await HttpSupport.WaitForTcp6Async(p2, TimeSpan.FromSeconds(10));
				Console.WriteLine("[s12] host port {0}: ipv4={1} ipv6={2}", p2, v4, v6);
			}

			c1.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
			c1.Delete(DeleteContainerOption.None);
			c1.Dispose();
			c2.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
			c2.Delete(DeleteContainerOption.None);
			c2.Dispose();
			Directory.Delete(www, recursive: true);
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s12] S12 complete.");
		return 0;
	}

	private static string Indent(string json)
	{
		try
		{
			return System.Text.Json.JsonSerializer.Serialize(
				System.Text.Json.JsonDocument.Parse(json).RootElement,
				new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
			);
		}
		catch (System.Text.Json.JsonException)
		{
			return json;
		}
	}
}
