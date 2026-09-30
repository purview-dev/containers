using Microsoft.WSL.Containers;

namespace WslcSpikes;

// Sfull: the documented end-to-end lifecycle as a sanity check of the managed API.
static class FullLifecycle
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== Sfull: documented end-to-end lifecycle ===");

		Session session = SpikeSupport.StartSession("wslcspike-full", memoryMb: 2048, cpuCount: 2);
		try
		{
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");
			Container container = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("hello-container"),
				new[] { "/bin/echo", "Hello from WSL Container!" },
				ProcessOutputMode.Event
			);

			int exitCode = await SpikeSupport.RunToExitAsync(container, TimeSpan.FromSeconds(30));
			Console.WriteLine("[sfull] init exited code={0}", exitCode);

			if (container.State == ContainerState.Running)
			{
				container.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
			}

			Console.WriteLine("[sfull] container state after stop: {0}", container.State);
			container.Delete(DeleteContainerOption.None);
			Console.WriteLine("[sfull] container deleted");
			container.Dispose();
		}
		catch (Exception ex)
		{
			SpikeSupport.Dump(ex);
			return 1;
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[sfull] Sfull complete.");
		return 0;
	}
}
