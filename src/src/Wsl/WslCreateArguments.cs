using System.Security.Cryptography;
using System.Text.Json;
using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

static class WslCreateArguments
{
	internal static string ReuseHash(ContainerConfiguration configuration) =>
		Convert.ToHexString(
			SHA256.HashData(
				JsonSerializer.SerializeToUtf8Bytes(
					new
					{
						configuration.Image,
						configuration.Command,
						configuration.Entrypoint,
						configuration.User,
						Environment = configuration.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal),
						Labels = configuration.Labels.OrderBy(pair => pair.Key, StringComparer.Ordinal),
						configuration.Network,
						configuration.NetworkAliases,
						configuration.NetworkingMode,
						configuration.Hostname,
						configuration.DomainName,
						configuration.WorkingDirectory,
						configuration.BindMounts,
						configuration.NamedVolumes,
						configuration.PortBindings,
						configuration.Privileged,
						configuration.EnableGPU,
						configuration.ResourceMappings,
					}
				)
			)
		);

	internal static void ValidateEnvironment(ContainerConfiguration configuration)
	{
		if (
			configuration.Environment.Any(pair =>
				pair.Key.Contains('=', StringComparison.Ordinal)
				|| pair.Key.Contains('\n', StringComparison.Ordinal)
				|| pair.Key.Contains('\r', StringComparison.Ordinal)
				|| pair.Value.Contains('\n', StringComparison.Ordinal)
				|| pair.Value.Contains('\r', StringComparison.Ordinal)
			)
		)
			throw new ContainerNotSupportedException("WSLC CLI-only features require single-line environment values.");
	}

	internal static string[] Build(
		ContainerConfiguration configuration,
		string name,
		string hash,
		string owner,
		string? environmentFile
	)
	{
		List<string> args = ["create", "--name", name, "--pull", "never"];
		void Add(string option, string? value)
		{
			if (value is not null)
			{
				args.Add(option);
				args.Add(value);
			}
		}
		Add("--hostname", configuration.Hostname);
		Add("--domainname", configuration.DomainName);
		Add("--workdir", configuration.WorkingDirectory);
		Add("--user", configuration.User);
		Add(
			"--network",
			configuration.Network
				?? (configuration.NetworkingMode == Networking.ContainerNetworkingMode.None ? "none" : "bridge")
		);
		foreach (var alias in configuration.NetworkAliases)
			Add("--network-alias", alias);
		foreach (var label in configuration.Labels)
			Add("--label", $"{label.Key}={label.Value}");
		Add("--label", $"purview.containers.owner={owner}");
		if (configuration.Reuse)
			Add("--label", $"purview.containers.reuse={hash}");
		if (configuration.EnableGPU)
			Add("--gpus", "all");
		if (configuration.EnableAutoRemove)
			args.Add("--rm");
		if (configuration.Privileged)
			throw new ContainerNotSupportedException(
				"The WSLC CLI does not expose privileged creation. Do not combine privileged mode with CLI-only features."
			);
		Add("--env-file", environmentFile);
		foreach (var mount in configuration.BindMounts)
			Add("--volume", $"{mount.HostPath}:{mount.ContainerPath}{(mount.ReadOnly ? ":ro" : string.Empty)}");
		foreach (var volume in configuration.NamedVolumes)
			Add("--volume", $"{volume.Name}:{volume.ContainerPath}{(volume.ReadOnly ? ":ro" : string.Empty)}");
		foreach (var binding in configuration.PortBindings)
			Add(
				"--publish",
				$"{binding.HostAddress ?? "127.0.0.1"}:{binding.HostPort ?? 0}:{binding.ContainerPort}/{(binding.Protocol == Networking.PortProtocol.Udp ? "udp" : "tcp")}"
			);
		if (configuration.Entrypoint.Count > 0)
			Add("--entrypoint", configuration.Entrypoint[0]);
		args.Add(configuration.Image);
		args.AddRange(configuration.Entrypoint.Skip(1));
		args.AddRange(configuration.Command);
		return [.. args];
	}
}
