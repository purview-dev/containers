using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S8: Multiple containers started concurrently in one session, each with a distinct random host port.
static class ConcurrentContainers
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S8: concurrent containers ===");
		Session session = SpikeSupport.StartSession("wslcspike-s8");
		try
		{
			await SpikeSupport.PullImageAsync(session, HttpSupport.ServerImage);
			string www = Path.Combine(SpikeSupport.StorageRoot, "s8-www");
			if (Directory.Exists(www))
			{
				Directory.Delete(www, recursive: true);
			}

			Directory.CreateDirectory(www);
			File.WriteAllText(Path.Combine(www, "index.html"), "hello-concurrent\n");

			int count = 4;
			Container[] containers = new Container[count];
			ushort[] containerPorts = new ushort[count];
			for (int i = 0; i < count; i++)
			{
				ushort cport = (ushort)(8080 + i);
				containerPorts[i] = cport;
				int index = i;
				containers[i] = SpikeSupport.CreateContainer(
					session,
					HttpSupport.ServerImage,
					SpikeSupport.UniqueName($"s8-{index}"),
					HttpSupport.ServerArgs(cport),
					configure: settings =>
					{
						settings.NetworkingMode = ContainerNetworkingMode.Bridged;
						settings.Volumes = new List<ContainerVolume> { new ContainerVolume(www, "/tmp", false) };
						settings.PortMappings = new List<ContainerPortMapping>
						{
							new ContainerPortMapping(0, cport, PortProtocol.TCP),
						};
					}
				);
			}

			Console.WriteLine("[s8] starting {0} containers concurrently...", count);
			Task<string>[] starts = containers
				.Select(
					(c, i) =>
						Task.Run(() =>
						{
							try
							{
								c.Start();
								return $"s8-{i}: ok";
							}
							catch (Exception ex)
							{
								return $"s8-{i}: FAIL {ex.Message} (HResult=0x{ex.HResult:X8})";
							}
						})
				)
				.ToArray();
			string[] startResults = await Task.WhenAll(starts);
			foreach (string result in startResults)
			{
				Console.WriteLine("[s8] {0}", result);
			}

			Console.WriteLine("[s8] retrying any failed containers sequentially...");
			for (int i = 0; i < count; i++)
			{
				if (startResults[i].EndsWith(": ok"))
				{
					continue;
				}

				try
				{
					containers[i].Start();
					Console.WriteLine("[s8]   s8-{0} started on retry", i);
				}
				catch (Exception ex)
				{
					Console.WriteLine("[s8]   s8-{0} retry FAILED: {1}", i, ex.Message);
				}
			}

			int?[] hostPorts = containers.Select(c => HttpSupport.TryReadAssignedHostPort(c.Inspect())).ToArray();
			for (int i = 0; i < count; i++)
			{
				Console.WriteLine(
					"[s8] container s8-{0} (cport {1}) host port = {2}",
					i,
					containerPorts[i],
					hostPorts[i]?.ToString() ?? "<none>"
				);
			}

			bool distinct = hostPorts.Where(p => p is not null).Select(p => p!.Value).Distinct().Count() == count;
			Console.WriteLine("[s8] all host ports distinct = {0}", distinct);

			foreach (int? hostPort in hostPorts)
			{
				if (hostPort is int hp)
				{
					bool tcp = await HttpSupport.WaitForTcpAsync(hp, TimeSpan.FromSeconds(30));
					(int status, string body) = await HttpSupport.GetWithRetryAsync(hp);
					Console.WriteLine("[s8] host port {0}: tcp={1} http={2} body='{3}'", hp, tcp, status, body.Trim());
				}
			}

			for (int i = 0; i < count; i++)
			{
				containers[i].Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
				containers[i].Delete(DeleteContainerOption.None);
				containers[i].Dispose();
			}

			Directory.Delete(www, recursive: true);
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s8] S8 complete.");
		return 0;
	}
}
