using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S11: Session thread-safety — concurrent pulls of the same image and concurrent create/start/stop/delete.
static class ThreadSafety
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S11: session thread-safety ===");
		Session session = SpikeSupport.StartSession("wslcspike-s11");
		try
		{
			Console.WriteLine("[s11] 4 concurrent pulls of the same image");
			string image = "docker.io/library/alpine:latest";
			Task<string>[] pulls = Enumerable
				.Range(0, 4)
				.Select(i =>
					Task.Run(async () =>
					{
						try
						{
							await SpikeSupport.PullImageAsync(session, image);
							return $"pull {i}: ok";
						}
						catch (Exception ex)
						{
							return $"pull {i}: FAIL {ex.Message} (HResult=0x{ex.HResult:X8})";
						}
					})
				)
				.ToArray();
			string[] pullResults = await Task.WhenAll(pulls);
			foreach (string result in pullResults)
			{
				Console.WriteLine("[s11] {0}", result);
			}

			Console.WriteLine("[s11] 8 concurrent create+start+stop+delete");
			int count = 8;
			Task<string>[] ops = Enumerable
				.Range(0, count)
				.Select(i =>
					Task.Run(async () =>
					{
						try
						{
							Container container = SpikeSupport.CreateContainer(
								session,
								image,
								SpikeSupport.UniqueName($"s11-{i}"),
								new[] { "/bin/sleep", "10" }
							);
							container.Start();
							await Task.Delay(300);
							container.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
							container.Delete(DeleteContainerOption.None);
							container.Dispose();
							return $"op {i}: ok";
						}
						catch (Exception ex)
						{
							return $"op {i}: FAIL {ex.Message} (HResult=0x{ex.HResult:X8})";
						}
					})
				)
				.ToArray();
			string[] opResults = await Task.WhenAll(ops);
			foreach (string result in opResults)
			{
				Console.WriteLine("[s11] {0}", result);
			}
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s11] S11 complete.");
		return 0;
	}
}
