using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

static class WslCommand
{
	internal sealed record Result(int ExitCode, byte[] Output, string Error);

	/// <summary>
	/// The wslc CLI to run. WSL installs it to <c>%ProgramFiles%\WSL\wslc.exe</c> and adds that folder to
	/// the machine PATH, but a process started before WSL was installed or updated (a long-lived IDE, or a
	/// CI step that installs WSL earlier in the same job) still has the old PATH. Prefer PATH, so an
	/// explicit override wins, and fall back to the install location.
	/// </summary>
	internal static string Executable { get; } = Resolve();

	/// <summary>The default install location of wslc, or <c>null</c> when it cannot be derived.</summary>
	static string? InstalledPath =>
		Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) is { Length: > 0 } programFiles
			? Path.Combine(programFiles, "WSL", "wslc.exe")
			: null;

	static string Resolve()
	{
		var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			try
			{
				if (File.Exists(Path.Combine(directory.Trim('"'), "wslc.exe")))
				{
					return "wslc";
				}
			}
			catch (ArgumentException)
			{
				// A malformed PATH entry; skip it.
			}
		}

		return InstalledPath is { } installed && File.Exists(installed) ? installed : "wslc";
	}

	[SuppressMessage(
		"Reliability",
		"CA2025",
		Justification = "Cancellation kills and drains the owned process; bounded cleanup then closes its pipes if the runtime stalls."
	)]
	internal static async Task<Result> RunAsync(
		string? session,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken,
		Stream? input = null,
		bool allowFailure = false,
		bool captureOutput = true
	)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ProcessStartInfo info = new(Executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = input is not null,
		};
		if (!string.IsNullOrEmpty(session))
		{
			info.ArgumentList.Add("--session");
			info.ArgumentList.Add(session);
		}

		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);
		System.Diagnostics.Process process;
		try
		{
			process = System.Diagnostics.Process.Start(info)
				?? throw new ContainerPrerequisiteException($"Could not start '{Executable}'.");
		}
		catch (Win32Exception exception)
		{
			throw new ContainerPrerequisiteException(
				"This feature (entrypoint/user/label overrides, file copy, image builds or reuse) needs the "
					+ $"wslc CLI, which ships with WSL {WslRequirements.MinimumWslVersion} or later. It was not "
					+ $"found on PATH or at '{InstalledPath ?? @"%ProgramFiles%\WSL\wslc.exe"}' "
					+ $"({exception.Message}). Run 'wsl --update'."
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
