using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Purview.Containers.Environment;

/// <summary>
/// Resolves bind sources against the selected daemon, verifying that both sides see the same files.
/// Does not change Docker contexts, environment variables, port forwarding or machine configuration.
/// </summary>
public sealed class ContainerEnvironmentResolver : IDisposable
{
	static readonly HashSet<string> BooleanRunOptions = new(StringComparer.Ordinal)
	{
		"--detach",
		"--disable-content-trust",
		"--help",
		"--init",
		"--interactive",
		"--oom-kill-disable",
		"--privileged",
		"--publish-all",
		"--read-only",
		"--rm",
		"--sig-proxy",
		"--tty",
		"--use-api-socket",
	};
	static readonly ConcurrentDictionary<string, ContainerEnvironmentResolver> Environments = new(
		StringComparer.Ordinal
	);
	readonly ContainerEnvironmentOptions _options;
	readonly ContainerPathMapping[] _mappings;
	readonly ConcurrentDictionary<string, string> _directories = new(
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
	);
	readonly SemaphoreSlim _resolutionLock = new(1, 1);
	readonly Func<string, string, CancellationToken, Task<bool>> _probe;
	readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _wslCandidates;

	/// <summary>Creates an isolated resolver. Keep it for the duration of a run to cache verified mappings.</summary>
	public ContainerEnvironmentResolver(ContainerEnvironmentOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentException.ThrowIfNullOrWhiteSpace(options.RuntimeCommand);
		ArgumentException.ThrowIfNullOrWhiteSpace(options.ProbeImage);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.CommandTimeout, TimeSpan.Zero);
		_options = new()
		{
			RuntimeCommand = options.RuntimeCommand,
			ProbeImage = options.ProbeImage,
			CommandTimeout = options.CommandTimeout,
			WslDistribution = options.WslDistribution,
		};
		_mappings = options
			.PathMappings.Select(pair => new ContainerPathMapping(pair.Key, pair.Value))
			.OrderByDescending(mapping => mapping.Specificity)
			.ToArray();
		_probe = ProbeAsync;
		_wslCandidates = GetWslCandidatesAsync;
	}

	internal ContainerEnvironmentResolver(
		ContainerEnvironmentOptions options,
		Func<string, string, CancellationToken, Task<bool>> probe,
		Func<string, CancellationToken, Task<IReadOnlyList<string>>> wslCandidates
	)
		: this(options)
	{
		_probe = probe;
		_wslCandidates = wslCandidates;
	}

	/// <summary>
	/// Gets a process-scoped shared resolver for a CLI. Reads CONTAINER_ENVIRONMENT_PATH_MAPPINGS (JSON),
	/// CONTAINER_ENVIRONMENT_WSL_DISTRIBUTION and CONTAINER_ENVIRONMENT_PROBE_IMAGE on first use.
	/// </summary>
	public static ContainerEnvironmentResolver ForRuntime(string runtimeCommand = "docker") =>
		Environments.GetOrAdd(
			runtimeCommand,
			command =>
			{
				ContainerEnvironmentOptions options = new()
				{
					RuntimeCommand = command,
					WslDistribution = System.Environment.GetEnvironmentVariable(
						"CONTAINER_ENVIRONMENT_WSL_DISTRIBUTION"
					),
					ProbeImage =
						System.Environment.GetEnvironmentVariable("CONTAINER_ENVIRONMENT_PROBE_IMAGE") ?? "alpine:3.19",
				};
				var mappings = System.Environment.GetEnvironmentVariable("CONTAINER_ENVIRONMENT_PATH_MAPPINGS");
				if (!string.IsNullOrWhiteSpace(mappings))
					options.PathMappings =
						JsonSerializer.Deserialize<Dictionary<string, string>>(mappings)
						?? throw new FormatException("CONTAINER_ENVIRONMENT_PATH_MAPPINGS must be a JSON object.");
				return new(options);
			}
		);

	/// <summary>Releases synchronization resources. Do not dispose instances returned by <see cref="ForRuntime"/>.</summary>
	public void Dispose() => _resolutionLock.Dispose();

	/// <summary>
	/// Resolves a local file or directory into a verified daemon bind source. Explicit mappings take
	/// precedence; otherwise native paths are tried before translations from running WSL distributions.
	/// </summary>
	public async Task<string> ResolveBindSourceAsync(string source, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(source);
		source = Path.GetFullPath(source);
		var isFile = File.Exists(source);
		var directory = isFile ? Path.GetDirectoryName(source) : source;
		if (!Directory.Exists(directory))
			throw new DirectoryNotFoundException($"Container bind source '{source}' does not exist on the client.");

		await _resolutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!_directories.TryGetValue(directory, out var resolved))
			{
				resolved = await ResolveDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
				_directories[directory] = resolved;
			}
			return isFile
				? resolved.TrimEnd('/', '\\')
					+ (resolved.Contains('\\', StringComparison.Ordinal) ? "\\" : "/")
					+ Path.GetFileName(source)
				: resolved;
		}
		finally
		{
			_resolutionLock.Release();
		}
	}

	/// <summary>
	/// Resolves <c>--mount</c> bind sources in a constructed <c>run</c> or <c>create</c> invocation.
	/// Volume mounts and arguments following the image name are left unchanged.
	/// </summary>
	public async Task ResolveBindMountsAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(startInfo);
		var arguments = startInfo.ArgumentList;
		var verb = arguments.Count > 0 && arguments[0] == "container" ? 1 : 0;
		if (verb >= arguments.Count || arguments[verb] is not ("run" or "create"))
			return;

		for (var i = verb + 1; i < arguments.Count; i++)
		{
			var argument = arguments[i];
			if (argument == "--" || !argument.StartsWith('-'))
				break;
			if (argument == "--mount" && i + 1 < arguments.Count)
			{
				i++;
				arguments[i] = await ResolveMountAsync(arguments[i], cancellationToken).ConfigureAwait(false);
			}
			else if (argument.StartsWith("--mount=", StringComparison.Ordinal))
			{
				arguments[i] =
					"--mount="
					+ await ResolveMountAsync(argument["--mount=".Length..], cancellationToken).ConfigureAwait(false);
			}
			else if (
				!argument.Contains('=', StringComparison.Ordinal)
				&& !BooleanRunOptions.Contains(argument)
				&& !(
					argument.Length > 1
					&& argument[1..].All(character => "ditP".Contains(character, StringComparison.Ordinal))
				)
			)
			{
				// Other Docker run options take a separate value, unless a short option embeds it.
				if (argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
					i++;
			}
		}
	}

	async Task<string> ResolveMountAsync(string value, CancellationToken cancellationToken)
	{
		var fields = ContainerBindMount.ParseFields(value).ToArray();
		if (!fields.Contains("type=bind", StringComparer.Ordinal))
			return value;

		for (var i = 0; i < fields.Length; i++)
		{
			if (
				fields[i].StartsWith("source=", StringComparison.Ordinal)
				|| fields[i].StartsWith("src=", StringComparison.Ordinal)
			)
			{
				var separator = fields[i].IndexOf('=', StringComparison.Ordinal);
				fields[i] =
					fields[i][..(separator + 1)]
					+ await ResolveBindSourceAsync(fields[i][(separator + 1)..], cancellationToken)
						.ConfigureAwait(false);
			}
		}
		return string.Join(",", fields.Select(ContainerBindMount.Quote));
	}

	async Task<string> ResolveDirectoryAsync(string directory, CancellationToken cancellationToken)
	{
		var explicitPath = _mappings.Select(mapping => mapping.Map(directory)).FirstOrDefault(path => path is not null);
		if (explicitPath is not null)
		{
			if (await _probe(directory, explicitPath, cancellationToken).ConfigureAwait(false))
				return explicitPath;
			throw Unshared(directory);
		}

		if (await _probe(directory, directory, cancellationToken).ConfigureAwait(false))
			return directory;

		var candidates = await _wslCandidates(directory, cancellationToken).ConfigureAwait(false);
		foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
		{
			if (await _probe(directory, candidate, cancellationToken).ConfigureAwait(false))
				return candidate;
		}
		throw Unshared(directory);
	}

	InvalidOperationException Unshared(string directory) =>
		new(
			$"Container runtime '{_options.RuntimeCommand}' cannot share client directory '{directory}' with its daemon. "
				+ "A working Docker connection does not imply shared files. Configure CONTAINER_ENVIRONMENT_PATH_MAPPINGS "
				+ "(a JSON object of client roots to daemon roots), or CONTAINER_ENVIRONMENT_WSL_DISTRIBUTION for WSL. "
				+ "The selected daemon must see the same files; unshared remote filesystems need explicit synchronization."
		);

	async Task<bool> ProbeAsync(string directory, string daemonDirectory, CancellationToken cancellationToken)
	{
		var id = Guid.NewGuid().ToString("N");
		var name = "container-environment-" + id;
		var inputName = "." + name + ".in";
		var outputName = "." + name + ".out";
		var input = Path.Combine(directory, inputName);
		var output = Path.Combine(directory, outputName);
		try
		{
			await File.WriteAllTextAsync(input, id, cancellationToken).ConfigureAwait(false);
			var result = await RunAsync(
					_options.RuntimeCommand,
					[
						"run",
						"--rm",
						"--name",
						name,
						"--mount",
						new ContainerBindMount(daemonDirectory, "/probe").ToString(),
						_options.ProbeImage,
						"sh",
						"-c",
						"test \"$(cat \"/probe/$1\" 2>/dev/null)\" = \"$2\" || exit 42; "
							+ "printf '%s' \"$2\"; printf '%s' \"$2\" > \"/probe/$3\" || exit 43",
						"probe",
						inputName,
						id,
						outputName,
					],
					cancellationToken
				)
				.ConfigureAwait(false);
			if (
				result.ExitCode is not (0 or 42 or 43)
				&& !result.Error.Contains("mount", StringComparison.OrdinalIgnoreCase)
			)
			{
				throw new InvalidOperationException(
					$"Container environment probe using '{_options.RuntimeCommand}' and image '{_options.ProbeImage}' failed: "
						+ result.Error.Trim()
				);
			}
			return result.ExitCode == 0
				&& result.Output.Trim() == id
				&& File.Exists(output)
				&& await File.ReadAllTextAsync(output, cancellationToken).ConfigureAwait(false) == id;
		}
		finally
		{
			// A cancelled docker client does not necessarily stop the container it asked the daemon to run.
			try
			{
				await ContainerProcess
					.RunAsync(
						_options.RuntimeCommand,
						["rm", "--force", name],
						TimeSpan.FromSeconds(5),
						CancellationToken.None
					)
					.ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is Win32Exception or TimeoutException or InvalidOperationException)
			{
				Trace.TraceWarning("Could not remove container environment probe '{0}': {1}", name, ex.Message);
			}
			finally
			{
				File.Delete(input);
				File.Delete(output);
			}
		}
	}

	async Task<IReadOnlyList<string>> GetWslCandidatesAsync(string directory, CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows())
			return [];

		try
		{
			var running = await ContainerProcess
				.RunAsync(
					"wsl.exe",
					["--list", "--quiet", "--running"],
					_options.CommandTimeout,
					cancellationToken,
					unicode: true
				)
				.ConfigureAwait(false);
			var distributions = running
				.Output.Split(['\r', '\n', '\0'], StringSplitOptions.RemoveEmptyEntries)
				.Select(value => value.Trim())
				.Where(value => value.Length > 0);
			if (!string.IsNullOrWhiteSpace(_options.WslDistribution))
			{
				if (!distributions.Contains(_options.WslDistribution, StringComparer.Ordinal))
					throw new InvalidOperationException(
						$"WSL distribution '{_options.WslDistribution}' is not running. Start it before running container tests."
					);
				distributions = [_options.WslDistribution];
			}

			List<string> candidates = [];
			foreach (var distribution in distributions)
			{
				var result = await RunAsync(
						"wsl.exe",
						["--distribution", distribution, "--exec", "wslpath", "-a", "-u", directory],
						cancellationToken
					)
					.ConfigureAwait(false);
				if (result.ExitCode == 0 && result.Output.Trim().StartsWith('/'))
					candidates.Add(result.Output.Trim());
			}
			return candidates;
		}
		catch (Win32Exception) when (string.IsNullOrWhiteSpace(_options.WslDistribution))
		{
			return [];
		}
	}

	Task<ContainerProcessResult> RunAsync(
		string command,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken
	) => ContainerProcess.RunAsync(command, arguments, _options.CommandTimeout, cancellationToken);
}
