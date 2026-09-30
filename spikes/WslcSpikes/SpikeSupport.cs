using System.Diagnostics;
using System.Text;
using Microsoft.WSL.Containers;
using Windows.Foundation;
using Process = Microsoft.WSL.Containers.Process;

namespace WslcSpikes;

static class SpikeSupport
{
	public static string StorageRoot =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcSpikes");

	// All spikes share one storage path so the per-path image store is reused (only one pull per image).
	public static string SharedStorage => Path.Combine(StorageRoot, "shared");

	public static string RunSuffix { get; } = Guid.NewGuid().ToString("N")[..8];

	public static string UniqueName(string prefix) => $"{prefix}-{RunSuffix}";

	public static void PrintRuntimeInfo()
	{
		ServiceVersion version = WslcService.GetVersion();
		Console.WriteLine("[service] version: {0}.{1}.{2}", version.Major, version.Minor, version.Revision);
		IReadOnlyList<Component> missing = WslcService.GetMissingComponents();
		Console.WriteLine(
			"[service] missing components: {0}",
			string.Join(", ", missing.Select(c => c.ToString()).DefaultIfEmpty("<none>"))
		);
	}

	public static Session StartSession(
		string name,
		string? storagePath = null,
		uint? memoryMb = null,
		uint? cpuCount = null
	)
	{
		string path = storagePath ?? SharedStorage;
		SessionSettings settings = new SessionSettings(name, path) { MemorySizeInMB = memoryMb, CpuCount = cpuCount };
		Stopwatch sw = Stopwatch.StartNew();
		Session session = new Session(settings);
		session.Start();
		sw.Stop();
		Console.WriteLine("[session] started '{0}' storage={1} in {2} ms", name, path, sw.ElapsedMilliseconds);
		return session;
	}

	public static async Task PullImageAsync(Session session, string image, IProgress<ImageProgress>? progress = null)
	{
		Stopwatch sw = Stopwatch.StartNew();
		IAsyncActionWithProgress<ImageProgress> op = session.PullImageAsync(new PullImageOptions(image));
		if (progress is not null)
		{
			op.Progress = (_, p) => progress.Report(p);
		}

		await op;
		sw.Stop();
		Console.WriteLine("[image] pulled {0} in {1} ms", image, sw.ElapsedMilliseconds);
	}

	public static void PrintImages(Session session, string label)
	{
		IReadOnlyList<ImageInfo> images = session.GetImages();
		Console.WriteLine("[image] {0} ({1} entries):", label, images.Count);
		foreach (ImageInfo image in images)
		{
			Console.WriteLine("  {0} size={1} created={2:o}", image.Name, image.Size, image.CreatedTimestamp);
		}
	}

	public static Container CreateContainer(
		Session session,
		string image,
		string name,
		IReadOnlyList<string> commandLine,
		ProcessOutputMode outputMode = ProcessOutputMode.Event,
		Action<ContainerSettings>? configure = null
	)
	{
		ProcessSettings initProcess = new ProcessSettings
		{
			CommandLine = new List<string>(commandLine),
			OutputMode = outputMode,
		};
		ContainerSettings settings = new ContainerSettings(image)
		{
			Name = name,
			InitProcess = initProcess,
			EnableAutoRemove = false,
		};
		configure?.Invoke(settings);
		Container container = session.CreateContainer(settings);
		Console.WriteLine("[container] created '{0}' id={1}", name, container.Id);
		return container;
	}

	public static ProcessCapture Capture(Process process)
	{
		StringBuilder stdout = new StringBuilder();
		StringBuilder stderr = new StringBuilder();
		TaskCompletionSource<int> tcs = new TaskCompletionSource<int>(
			TaskCreationOptions.RunContinuationsAsynchronously
		);
		process.OutputReceived += data => stdout.Append(Encoding.UTF8.GetString(data));
		process.ErrorReceived += data => stderr.Append(Encoding.UTF8.GetString(data));
		process.Exited += code => tcs.TrySetResult(code);
		return new ProcessCapture(stdout, stderr, tcs.Task);
	}

	public static async Task<int> RunToExitAsync(Container container, TimeSpan? timeout = null)
	{
		ProcessCapture capture = Capture(container.InitProcess);
		Stopwatch sw = Stopwatch.StartNew();
		container.Start();
		Task first = await Task.WhenAny(capture.Exit, Task.Delay(timeout ?? TimeSpan.FromSeconds(90)))
			.ConfigureAwait(false);
		if (first != capture.Exit)
		{
			Console.Error.WriteLine("[container] timed out waiting for exit; signalling SIGKILL");
			container.InitProcess.Signal(Signal.SIGKILL);
		}

		int exit = await capture.Exit.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		sw.Stop();
		Console.WriteLine("[container] '{0}' exited code={1} in {2} ms", container.Id, exit, sw.ElapsedMilliseconds);
		Console.WriteLine("[container] stdout: {0}", capture.Stdout);
		Console.WriteLine("[container] stderr: {0}", capture.Stderr);
		return exit;
	}

	public static void Dump(Exception exception)
	{
		Console.WriteLine("[error] {0} (HResult=0x{1:X8})", exception.GetType().Name, exception.HResult);
		Console.WriteLine("  {0}", exception.Message);
		Exception? inner = exception.InnerException;
		while (inner is not null)
		{
			Console.WriteLine(
				"  inner: {0} (HResult=0x{1:X8}) {2}",
				inner.GetType().Name,
				inner.HResult,
				inner.Message
			);
			inner = inner.InnerException;
		}
	}

	public static void Cleanup(Session session)
	{
		try
		{
			session.Terminate();
			Console.WriteLine("[cleanup] session terminated");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[cleanup] terminate failed: {0}", ex.Message);
		}

		session.Dispose();
	}
}

sealed record ProcessCapture(StringBuilder Stdout, StringBuilder Stderr, Task<int> Exit);
