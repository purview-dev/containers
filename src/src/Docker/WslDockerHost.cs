using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DotNet.Testcontainers.Configurations;

namespace Purview.Containers.Docker;

/// <summary>Translates Windows bind sources only after identifying the daemon's local WSL distribution.</summary>
sealed class WslDockerHost(string distribution, Func<string[], string?> run)
{
	internal static bool IsWindowsDrivePath(string path) =>
		path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';

	internal string Resolve(string path)
	{
		if (!IsWindowsDrivePath(path))
		{
			return path;
		}

		var translated = run([
			"--distribution",
			distribution,
			"--exec",
			"wslpath",
			"-a",
			"-u",
			path.Replace('\\', '/'),
		]);
		if (
			string.IsNullOrWhiteSpace(translated)
			|| !translated.StartsWith('/')
			|| translated.Contains('\n', StringComparison.Ordinal)
		)
		{
			throw new InvalidOperationException(
				$"Could not translate a bind mount into WSL distribution '{distribution}'."
			);
		}

		return translated;
	}

	internal static WslDockerHost? Detect()
	{
		var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;
		if (!OperatingSystem.IsWindows() || !IsLocalTcp(endpoint.Endpoint))
		{
			return null;
		}

		using var client = endpoint.GetDockerClientBuilder(Guid.NewGuid()).Build();
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
		var info = client.System.GetSystemInfoAsync(timeout.Token).ConfigureAwait(false).GetAwaiter().GetResult();
		var distribution = FindDistribution(endpoint.Endpoint, info.KernelVersion, info.OperatingSystem, info.ID, Run);

		return distribution is null ? null : new WslDockerHost(distribution, Run);
	}

	internal static string? FindDistribution(
		Uri endpoint,
		string? kernelVersion,
		string? operatingSystem,
		string? daemonId,
		Func<string[], string?> run
	)
	{
		if (
			!IsLocalTcp(endpoint)
			|| kernelVersion?.Contains("microsoft", StringComparison.OrdinalIgnoreCase) != true
			|| operatingSystem?.Contains("Docker Desktop", StringComparison.OrdinalIgnoreCase) == true
			|| string.IsNullOrWhiteSpace(daemonId)
		)
		{
			return null;
		}

		var distributions = run(["--list", "--running", "--quiet"]);
		foreach (
			var candidate in (distributions ?? string.Empty).Split(
				['\r', '\n', '\0'],
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
			)
		)
		{
			// Compare daemon identities, not the default distro or a hostname: several distributions
			// can run different Docker engines under the same WSL kernel.
			var id = run([
				"--distribution",
				candidate,
				"--exec",
				"env",
				"-u",
				"DOCKER_HOST",
				"-u",
				"DOCKER_TLS_VERIFY",
				"-u",
				"DOCKER_CERT_PATH",
				"-u",
				"DOCKER_CONTEXT",
				"docker",
				"--host",
				"unix:///var/run/docker.sock",
				"info",
				"--format",
				"{{.ID}}",
			]);
			if (string.Equals(id, daemonId, StringComparison.Ordinal))
			{
				return candidate;
			}
		}

		return null;
	}

	static bool IsLocalTcp(Uri endpoint) => endpoint.Scheme is "tcp" or "http" or "https" && endpoint.IsLoopback;

	static string? Run(string[] arguments)
	{
		try
		{
			ProcessStartInfo startInfo = new("wsl.exe")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = arguments[0] == "--list" ? Encoding.Unicode : Encoding.UTF8,
			};
			foreach (var argument in arguments)
			{
				startInfo.ArgumentList.Add(argument);
			}

			using var process = Process.Start(startInfo);
			if (process is null)
			{
				return null;
			}

			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(5000))
			{
				process.Kill(entireProcessTree: true);
				process.WaitForExit();
				return null;
			}

			Task.WhenAll(output, error).ConfigureAwait(false).GetAwaiter().GetResult();
			return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : null;
		}
		catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
		{
			return null;
		}
	}
}
