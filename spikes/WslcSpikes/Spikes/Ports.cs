using System.Text.Json;
using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S4: Port mappings — confirms Bridged is required, that random host ports work via windowsPort=0,
// records the Inspect() schema, and verifies host reachability.
static class Ports
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S4: Port mappings ===");
		Session session = SpikeSupport.StartSession("wslcspike-s4");
		try
		{
			await SpikeSupport.PullImageAsync(session, HttpSupport.ServerImage);
			string www = PrepareWww();

			Console.WriteLine("[s4] probe A: volume only, default networking");
			Container probeA = Create(
				session,
				SpikeSupport.UniqueName("s4-probeA"),
				s => s.Volumes = new List<ContainerVolume> { new ContainerVolume(www, "/tmp", false) }
			);
			probeA.Start();
			Console.WriteLine("[s4] probeA started state={0} (NetworkMode in inspect should be 'none')", probeA.State);

			Console.WriteLine("[s4] probe B: fixed host port + Bridged");
			ushort portB = HttpSupport.GetFreeTcpPort();
			Container probeB = Create(
				session,
				SpikeSupport.UniqueName("s4-probeB"),
				s =>
				{
					s.NetworkingMode = ContainerNetworkingMode.Bridged;
					s.PortMappings = new List<ContainerPortMapping>
					{
						new ContainerPortMapping(portB, 8080, PortProtocol.TCP),
					};
				}
			);
			probeB.Start();
			(bool tcpB, (int statusB, string bodyB)) = await ProbeAsync(portB);

			Console.WriteLine("[s4] probe C: random host port (windowsPort=0) + Bridged + volume");
			Container probeC = Create(
				session,
				SpikeSupport.UniqueName("s4-random"),
				s =>
				{
					s.NetworkingMode = ContainerNetworkingMode.Bridged;
					s.Volumes = new List<ContainerVolume> { new ContainerVolume(www, "/tmp", false) };
					s.PortMappings = new List<ContainerPortMapping>
					{
						new ContainerPortMapping(0, 8080, PortProtocol.TCP),
					};
				}
			);
			probeC.Start();
			string randomInspect = probeC.Inspect();
			int? assigned = HttpSupport.TryReadAssignedHostPort(randomInspect);
			Console.WriteLine("[s4] random assigned host port = {0}", assigned?.ToString() ?? "<none>");
			(bool tcpC, (int statusC, string bodyC)) = assigned is int pc
				? await ProbeAsync(pc)
				: (false, (0, string.Empty));

			Console.WriteLine("[s4] probe D: fixed host port + Bridged + WindowsAddress=127.0.0.1");
			ushort portD = HttpSupport.GetFreeTcpPort();
			Container probeD = Create(
				session,
				SpikeSupport.UniqueName("s4-probeD"),
				s =>
				{
					s.NetworkingMode = ContainerNetworkingMode.Bridged;
					s.PortMappings = new List<ContainerPortMapping>
					{
						new ContainerPortMapping(portD, 8080, PortProtocol.TCP)
						{
							WindowsAddress = new Windows.Networking.HostName("127.0.0.1"),
						},
					};
				}
			);
			probeD.Start();
			(bool tcpD, _) = await ProbeAsync(portD);

			Console.WriteLine(
				"[s4] summary: fixedPort={0} tcp={1} http={2} body='{3}'",
				portB,
				tcpB,
				statusB,
				bodyB.Trim()
			);
			Console.WriteLine(
				"[s4] summary: randomPort={0} tcp={1} http={2} body='{3}'",
				assigned?.ToString() ?? "?",
				tcpC,
				statusC,
				bodyC.Trim()
			);
			Console.WriteLine("[s4] summary: explicitAddrPort={0} tcp={1}", portD, tcpD);

			Console.WriteLine("[s4] Inspect() schema for the running random-mapped container:");
			Console.WriteLine(Indent(randomInspect));

			Cleanup(probeA);
			Cleanup(probeB);
			Cleanup(probeC);
			Cleanup(probeD);
			Directory.Delete(www, recursive: true);
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s4] S4 complete.");
		return 0;
	}

	private static Container Create(Session session, string name, Action<ContainerSettings> configure)
	{
		return SpikeSupport.CreateContainer(
			session,
			HttpSupport.ServerImage,
			name,
			HttpSupport.ServerArgs(8080),
			configure: configure
		);
	}

	private static async Task<(bool Tcp, (int Status, string Body))> ProbeAsync(int port)
	{
		bool tcp = await HttpSupport.WaitForTcpAsync(port, TimeSpan.FromSeconds(30));
		(int status, string body) = await HttpSupport.GetWithRetryAsync(port);
		Console.WriteLine("[s4] port {0}: tcp={1} http={2} body='{3}'", port, tcp, status, body.Trim());
		return (tcp, (status, body));
	}

	private static void Cleanup(Container container)
	{
		try
		{
			container.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
		}
		catch { }

		try
		{
			container.Delete(DeleteContainerOption.Force);
		}
		catch { }

		container.Dispose();
	}

	private static string PrepareWww()
	{
		string dir = Path.Combine(SpikeSupport.StorageRoot, "s4-www");
		if (Directory.Exists(dir))
		{
			Directory.Delete(dir, recursive: true);
		}

		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "index.html"), "hello-from-wslc\n");
		return dir;
	}

	private static string Indent(string json)
	{
		try
		{
			return JsonSerializer.Serialize(
				JsonDocument.Parse(json).RootElement,
				new JsonSerializerOptions { WriteIndented = true }
			);
		}
		catch (JsonException)
		{
			return json;
		}
	}
}
