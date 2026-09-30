using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S6: Bind mounts (ContainerVolume) and named volumes (ContainerNamedVolume) — auto-provisioned
// vs explicitly pre-created via CreateVhdVolume.
static class MountsAndVolumes
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S6: mounts + named volumes ===");
		Session session = SpikeSupport.StartSession("wslcspike-s6");
		try
		{
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");

			Console.WriteLine("[s6] bind mount: read a file from a Windows directory");
			string hostDir = Path.Combine(SpikeSupport.StorageRoot, "s6-hostdir");
			if (Directory.Exists(hostDir))
			{
				Directory.Delete(hostDir, recursive: true);
			}

			Directory.CreateDirectory(hostDir);
			File.WriteAllText(Path.Combine(hostDir, "hello.txt"), "hello-from-host\n");

			Container m1 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s6-bind"),
				new[] { "/bin/cat", "/mnt/data/hello.txt" },
				configure: settings =>
					settings.Volumes = new List<ContainerVolume> { new ContainerVolume(hostDir, "/mnt/data", false) }
			);
			await SpikeSupport.RunToExitAsync(m1, TimeSpan.FromSeconds(30));
			m1.Delete(DeleteContainerOption.None);
			m1.Dispose();

			Console.WriteLine("[s6] bind mount read-only: attempt to write should fail");
			Container m2 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s6-bind-ro"),
				new[] { "/bin/sh", "-c", "touch /mnt/data/newfile.txt; echo exit=$?" },
				configure: settings =>
					settings.Volumes = new List<ContainerVolume> { new ContainerVolume(hostDir, "/mnt/data", true) }
			);
			await SpikeSupport.RunToExitAsync(m2, TimeSpan.FromSeconds(30));
			m2.Delete(DeleteContainerOption.None);
			m2.Dispose();
			Console.WriteLine(
				"[s6] hostdir now contains newfile.txt? {0}",
				File.Exists(Path.Combine(hostDir, "newfile.txt"))
			);

			Console.WriteLine("[s6] named volume AUTO-provisioned: write in one container, read in another");
			string autoVol = SpikeSupport.UniqueName("s6vol");
			Container n1 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s6-vol-writer"),
				new[] { "/bin/sh", "-c", "echo shared-data > /vol/data.txt; sleep 60" },
				configure: settings =>
					settings.NamedVolumes = new List<ContainerNamedVolume>
					{
						new ContainerNamedVolume(autoVol, "/vol", false),
					}
			);
			n1.Start();
			await Task.Delay(2000);

			Container n2 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s6-vol-reader"),
				new[] { "/bin/cat", "/vol/data.txt" },
				configure: settings =>
					settings.NamedVolumes = new List<ContainerNamedVolume>
					{
						new ContainerNamedVolume(autoVol, "/vol", false),
					}
			);
			await SpikeSupport.RunToExitAsync(n2, TimeSpan.FromSeconds(30));
			n2.Delete(DeleteContainerOption.None);
			n2.Dispose();
			n1.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
			n1.Delete(DeleteContainerOption.None);
			n1.Dispose();

			Console.WriteLine("[s6] named volume PRE-created via CreateVhdVolume, then referenced");
			string preVol = SpikeSupport.UniqueName("s6vol");
			try
			{
				session.CreateVhdVolume(new VhdOptions(preVol, 256UL * 1024 * 1024, VhdType.Dynamic));
				Console.WriteLine("[s6] CreateVhdVolume('{0}') succeeded", preVol);
			}
			catch (Exception ex)
			{
				SpikeSupport.Dump(ex);
			}

			Container n3 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s6-vol-prec"),
				new[] { "/bin/sh", "-c", "echo prec > /vol/x.txt; sleep 2" },
				configure: settings =>
					settings.NamedVolumes = new List<ContainerNamedVolume>
					{
						new ContainerNamedVolume(preVol, "/vol", false),
					}
			);
			await SpikeSupport.RunToExitAsync(n3, TimeSpan.FromSeconds(30));
			n3.Delete(DeleteContainerOption.None);
			n3.Dispose();

			try
			{
				session.DeleteVhdVolume(preVol);
				Console.WriteLine("[s6] DeleteVhdVolume('{0}') succeeded", preVol);
			}
			catch (Exception ex)
			{
				SpikeSupport.Dump(ex);
			}

			Directory.Delete(hostDir, recursive: true);
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s6] S6 complete.");
		return 0;
	}
}
