using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

static class WslCommand
{
	internal sealed record Result(int ExitCode, byte[] Output, string Error);

	[SuppressMessage(
		"Reliability",
		"CA2025",
		Justification = "Cancellation kills and drains the owned process; bounded cleanup then closes its pipes if the runtime stalls."
	)]
	internal static async Task<Result> RunAsync(
		string session,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken,
		Stream? input = null,
		bool allowFailure = false,
		bool captureOutput = true
	)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ProcessStartInfo info = new("wslc")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = input is not null,
		};
		info.ArgumentList.Add("--session");
		info.ArgumentList.Add(session);
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);
		System.Diagnostics.Process process;
		try
		{
			process =
				System.Diagnostics.Process.Start(info)
				?? throw new ContainerPrerequisiteException("Could not start wslc.");
		}
		catch (Win32Exception)
		{
			throw new ContainerPrerequisiteException(
				"This feature requires the wslc CLI on PATH. Install/update WSL Containers."
			);
		}
		using (process)
		{
			var read = ReadOutputAsync(process.StandardOutput.BaseStream, captureOutput, cancellationToken);
			var error = process.StandardError.ReadToEndAsync(cancellationToken);
			var write = input is null ? Task.CompletedTask : WriteInputAsync(process, input, cancellationToken);
			var pending = Task.WhenAll(process.WaitForExitAsync(cancellationToken), read, error, write);
			try
			{
				await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
				if (process.ExitCode != 0 && !allowFailure)
				{
					// CLI errors can echo arguments, environment values or Dockerfile output.
					throw new ContainerException(
						$"WSLC '{arguments[0]}' failed with exit code {process.ExitCode}. No command output is included because it may contain secrets."
					);
				}
				return new(process.ExitCode, await read.ConfigureAwait(false), await error.ConfigureAwait(false));
			}
			catch
			{
				try
				{
					if (!process.HasExited)
						process.Kill(entireProcessTree: true);
				}
				catch (InvalidOperationException) { }
				catch (Win32Exception) { }
				try
				{
					await pending.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception exception)
					when (exception
							is OperationCanceledException
								or TimeoutException
								or IOException
								or InvalidOperationException
					)
				{
					_ = pending.ContinueWith(
						static task => _ = task.Exception,
						CancellationToken.None,
						TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
						TaskScheduler.Default
					);
				}
				throw;
			}
		}
	}

	static async Task<byte[]> ReadOutputAsync(Stream stream, bool capture, CancellationToken cancellationToken)
	{
		using MemoryStream output = new();
		await stream.CopyToAsync(capture ? output : Stream.Null, cancellationToken).ConfigureAwait(false);
		return output.ToArray();
	}

	static async Task WriteInputAsync(
		System.Diagnostics.Process process,
		Stream input,
		CancellationToken cancellationToken
	)
	{
		try
		{
			await input.CopyToAsync(process.StandardInput.BaseStream, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			process.StandardInput.Close();
		}
	}
}
