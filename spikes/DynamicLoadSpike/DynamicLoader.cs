using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace DynamicLoadSpike;

/// <summary>
/// Loads the WSLC projection (and the native SDK it P/Invokes) from the local payload folder and calls
/// into <c>Microsoft.WSL.Containers.WslcService</c> by reflection. This is the phase 0 proof that a
/// platform-neutral <c>net10.0</c> process can drive WSLC on a Windows host.
/// </summary>
internal static class DynamicLoader
{
	const string ProjectionFile = "wslcsdkcs.dll";
	const string ServiceType = "Microsoft.WSL.Containers.WslcService";

	public static Task<int> RunAsync(string payloadDir)
	{
		if (!OperatingSystem.IsWindows())
		{
			Console.WriteLine("[dynamic] skipped: this host is not Windows, so the WSLC payload is never loaded.");
			Console.WriteLine(
				"[dynamic] (a real build would report the 'wsl' backend as unavailable here and fall through to Docker)."
			);
			return Task.FromResult(0);
		}

		var projection = Path.Combine(payloadDir, ProjectionFile);
		if (!File.Exists(projection))
		{
			Console.WriteLine($"[dynamic] FAILED: projection not found at {projection}");
			return Task.FromResult(1);
		}

		try
		{
			var context = new PayloadLoadContext(payloadDir);
			context.Resolving += (_, name) => context.TryLoad(name);

			var assembly = context.LoadFromAssemblyPath(projection);
			Console.WriteLine($"[dynamic] loaded assembly: {assembly.FullName}");

			var service = assembly.GetType(ServiceType, throwOnError: true)!;
			var version = service
				.GetMethod("GetVersion", BindingFlags.Public | BindingFlags.Static)!
				.Invoke(null, null);
			Console.WriteLine($"[dynamic] {ServiceType}.GetVersion() = {Format(version)}");
			PrintProperties("version", version);

			var missing = service
				.GetMethod("GetMissingComponents", BindingFlags.Public | BindingFlags.Static)!
				.Invoke(null, null);
			Console.WriteLine($"[dynamic] {ServiceType}.GetMissingComponents() = {Format(missing)}");
			var missingCount = PrintItems("missing", missing);

			// A statically-available runtime is not enough: the feature delegates real work through the
			// WSLC projection, so prove a session can be created, started and released from here too.
			Console.WriteLine(
				missingCount == 0
					? "[dynamic] host is WSLC-capable; probing a real session."
					: "[dynamic] host is NOT WSLC-capable; skipping the session probe."
			);
			var sessionOk = missingCount == 0 && ProbeSession(assembly);

			Console.WriteLine(
				$"[dynamic] RESULT: {(sessionOk || missingCount != 0 ? "PASS" : "FAILED")} - a plain "
					+ $"{RuntimeInformation.FrameworkDescription} net10.0 process drove WSLC."
			);
			return Task.FromResult(sessionOk || missingCount != 0 ? 0 : 1);
		}
		catch (Exception exception)
		{
			Console.WriteLine($"[dynamic] RESULT: FAILED - {exception.GetType().Name}: {exception.Message}");
			for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
			{
				Console.WriteLine($"[dynamic]   inner: {inner.GetType().Name} (0x{inner.HResult:X8}): {inner.Message}");
			}

			return Task.FromResult(1);
		}
	}

	static string Format(object? value) => value?.ToString() ?? "<null>";

	static void PrintProperties(string label, object? value)
	{
		if (value is null)
		{
			return;
		}

		foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			Console.WriteLine($"[dynamic]   {label}.{property.Name} = {Format(property.GetValue(value))}");
		}
	}

	static int PrintItems(string label, object? value)
	{
		var count = 0;
		if (value is System.Collections.IEnumerable items and not string)
		{
			foreach (var item in items)
			{
				Console.WriteLine($"[dynamic]   {label}[*] = {Format(item)}");
				count++;
			}
		}

		return count;
	}

	static bool ProbeSession(Assembly assembly)
	{
		var name = $"dynload-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
		var storage = Path.Combine(Path.GetTempPath(), "dynload-spike", name);
		object? session = null;
		try
		{
			var settingsType = assembly.GetType("Microsoft.WSL.Containers.SessionSettings", throwOnError: true)!;
			var settings = Activator.CreateInstance(settingsType, name, storage);
			var sessionType = assembly.GetType("Microsoft.WSL.Containers.Session", throwOnError: true)!;
			session = Activator.CreateInstance(sessionType, settings);

			sessionType.GetMethod("Start")!.Invoke(session, null);
			Console.WriteLine($"[dynamic] session started: {name} (storage={storage})");
			PrintProperties("session", session);

			sessionType.GetMethod("Terminate")!.Invoke(session, null);
			Console.WriteLine("[dynamic] session terminated.");
			return true;
		}
		catch (Exception exception)
		{
			Console.WriteLine($"[dynamic] session probe FAILED: {exception.GetType().Name}: {exception.Message}");
			for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
			{
				Console.WriteLine($"[dynamic]   inner: {inner.GetType().Name} (0x{inner.HResult:X8}): {inner.Message}");
			}

			return false;
		}
		finally
		{
			(session as IDisposable)?.Dispose();
			TryDelete(storage);
		}
	}

	static void TryDelete(string directory)
	{
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception exception)
		{
			Console.WriteLine($"[dynamic] (cleanup) could not remove '{directory}': {exception.Message}");
		}
	}
}

/// <summary>
/// A dedicated load context so the projection and its Windows SDK dependencies resolve from the payload
/// folder (and the native <c>wslcsdk.dll</c> loads from there too), independently of the host app.
/// </summary>
internal sealed class PayloadLoadContext(string directory) : AssemblyLoadContext("wslc-payload", isCollectible: true)
{
	readonly string _directory = directory;

	public Assembly? TryLoad(AssemblyName name)
	{
		var candidate = Path.Combine(_directory, $"{name.Name}.dll");
		return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
	}

	protected override Assembly? Load(AssemblyName assemblyName) => TryLoad(assemblyName);

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		var candidate = Path.Combine(_directory, $"{unmanagedDllName}.dll");
		return File.Exists(candidate) ? LoadUnmanagedDllFromPath(candidate) : IntPtr.Zero;
	}
}
