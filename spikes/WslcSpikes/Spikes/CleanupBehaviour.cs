using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S7: Stop/delete idempotency, error HResults, and force-delete while running.
static class CleanupBehaviour
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S7: cleanup idempotency + HResults ===");
		Session session = SpikeSupport.StartSession("wslcspike-s7");
		try
		{
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");

			Console.WriteLine("[s7] case 1: stop then stop again");
			string nameA = SpikeSupport.UniqueName("s7a");
			Container c1 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				nameA,
				new[] { "/bin/sleep", "300" }
			);
			c1.Start();
			Console.WriteLine("[s7] started state={0}", c1.State);
			c1.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
			Console.WriteLine("[s7] after stop state={0}", c1.State);
			try
			{
				c1.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
				Console.WriteLine("[s7] second Stop succeeded (unexpected)");
			}
			catch (Exception ex)
			{
				SpikeSupport.Dump(ex);
			}

			Console.WriteLine("[s7] case 2: delete then delete again");
			c1.Delete(DeleteContainerOption.None);
			Console.WriteLine("[s7] first Delete succeeded");
			try
			{
				c1.Delete(DeleteContainerOption.None);
				Console.WriteLine("[s7] second Delete succeeded (unexpected)");
			}
			catch (Exception ex)
			{
				SpikeSupport.Dump(ex);
			}

			Console.WriteLine("[s7] case 3: OpenContainer of deleted container");
			try
			{
				using Container opened = session.OpenContainer(nameA, ProcessOutputMode.Event);
				Console.WriteLine("[s7] OpenContainer succeeded (unexpected) id={0}", opened.Id);
			}
			catch (Exception ex)
			{
				SpikeSupport.Dump(ex);
			}

			c1.Dispose();

			Console.WriteLine("[s7] case 4: Force delete while running");
			string nameB = SpikeSupport.UniqueName("s7b");
			Container c2 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				nameB,
				new[] { "/bin/sleep", "300" }
			);
			c2.Start();
			Console.WriteLine("[s7] started state={0}", c2.State);
			try
			{
				c2.Delete(DeleteContainerOption.Force);
				Console.WriteLine("[s7] Force delete while running succeeded, state={0}", c2.State);
			}
			catch (Exception ex)
			{
				SpikeSupport.Dump(ex);
			}

			c2.Dispose();

			Console.WriteLine("[s7] case 5: OpenContainer of running container, then operate via handle");
			string nameC = SpikeSupport.UniqueName("s7c");
			Container c3 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				nameC,
				new[] { "/bin/sleep", "300" }
			);
			c3.Start();
			using (Container reopened = session.OpenContainer(nameC, ProcessOutputMode.Event))
			{
				Console.WriteLine("[s7] reopened id={0} state={1}", reopened.Id, reopened.State);
				reopened.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10));
				Console.WriteLine("[s7] reopened stop, state={0}", reopened.State);
				reopened.Delete(DeleteContainerOption.None);
				Console.WriteLine("[s7] reopened delete succeeded");
			}

			c3.Dispose();
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s7] S7 complete.");
		return 0;
	}
}
