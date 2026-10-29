using System.Diagnostics;

namespace Purview.Containers.Environment;

sealed record ContainerProcessResult(int ExitCode, string Output, string Error);

static class ContainerProcess
{
	internal static async Task<ContainerProcessResult> RunAsync(
		string command,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken,
		bool unicode = false
	)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(timeout);

		ProcessStartInfo startInfo = new(command)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		if (unicode)
			startInfo.StandardOutputEncoding = System.Text.Encoding.Unicode;
		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		using var process =
			Process.Start(startInfo)
			?? throw new InvalidOperationException($"Could not start container environment command '{command}'.");

		try
		{
			// Both pipes must be drained concurrently, including while a runtime pulls the probe image.
			var output = process.StandardOutput.ReadToEndAsync(cts.Token);
			var error = process.StandardError.ReadToEndAsync(cts.Token);
			await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
			return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
		}
		catch (OperationCanceledException)
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
			}
			if (cancellationToken.IsCancellationRequested)
				throw;
			throw new TimeoutException(
				$"Container environment command '{command}' exceeded {timeout.TotalSeconds:0}s."
			);
		}
	}
}
