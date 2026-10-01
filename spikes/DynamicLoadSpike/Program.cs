using System.Reflection;
using System.Runtime.Versioning;
using DynamicLoadSpike;

// Phase 0 feasibility probe for "dynamic WSLC from a platform-neutral TFM".
//
//   dotnet run --project spikes/DynamicLoadSpike
//
// The static inspection runs on any OS; the dynamic load only runs on Windows. Exit code is 0 when
// the probe reached a verdict (even if the verdict is "WSLC unavailable here") and 1 only when the
// harness itself failed, so the run is useful on Linux CI as well.

var payload = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "payload"));

Console.WriteLine("[env] ---------------------------------------------------------------");
Console.WriteLine(
	$"[env] tfm        : {typeof(Program).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName}"
);
Console.WriteLine($"[env] runtime    : {Environment.Version}");
Console.WriteLine($"[env] isWindows  : {OperatingSystem.IsWindows()}");
Console.WriteLine($"[env] is64bit    : {Environment.Is64BitProcess}");
Console.WriteLine($"[env] payloadDir : {payload}");
Console.WriteLine();

Console.WriteLine("[static] ------------------------------------------------------------");
PayloadInspector.Inspect(Path.Combine(payload, "wslcsdkcs.dll"));
Console.WriteLine();

Console.WriteLine("[dynamic] -----------------------------------------------------------");
return await DynamicLoader.RunAsync(payload);
