using Microsoft.WSL.Containers;
using Purview.WslContainers.Mounts;
using Purview.WslContainers.Networking;
using Windows.Networking;

namespace Purview.WslContainers;

/// <summary>Maps the public configuration model onto the WSLC settings types.</summary>
static class WslSettingsMapper
{
	public static ContainerSettings ToContainerSettings(IContainerConfiguration configuration, string containerName)
	{
		ProcessSettings initProcess = new()
		{
			CommandLine = [.. configuration.Command],
			OutputMode = ProcessOutputMode.Event,
			WorkingDirectory = configuration.WorkingDirectory,
		};
		if (configuration.Environment.Count > 0)
		{
			initProcess.EnvironmentVariables = new Dictionary<string, string>(configuration.Environment);
		}

		ContainerSettings settings = new(configuration.Image)
		{
			Name = containerName,
			HostName = configuration.Hostname,
			DomainName = configuration.DomainName,
			InitProcess = initProcess,
			NetworkingMode = (Microsoft.WSL.Containers.ContainerNetworkingMode)configuration.NetworkingMode,
			Privileged = configuration.Privileged,
			EnableGpu = configuration.EnableGPU,
			EnableAutoRemove = configuration.EnableAutoRemove,
		};

		if (configuration.PortBindings.Count > 0)
		{
			settings.PortMappings = [.. configuration.PortBindings.Select(ToPortMapping)];
		}

		if (configuration.BindMounts.Count > 0)
		{
			settings.Volumes = [.. configuration.BindMounts.Select(ToVolume)];
		}

		if (configuration.NamedVolumes.Count > 0)
		{
			settings.NamedVolumes = [.. configuration.NamedVolumes.Select(ToNamedVolume)];
		}

		return settings;
	}

	static ContainerPortMapping ToPortMapping(PortBinding binding)
	{
		ContainerPortMapping mapping = new(
			binding.HostPort ?? 0,
			binding.ContainerPort,
			(Microsoft.WSL.Containers.PortProtocol)binding.Protocol
		);
		if (binding.HostAddress is not null)
		{
			mapping.WindowsAddress = new HostName(binding.HostAddress);
		}

		return mapping;
	}

	static ContainerVolume ToVolume(BindMount bindMount)
	{
		return new ContainerVolume(bindMount.HostPath, bindMount.ContainerPath, bindMount.ReadOnly);
	}

	static ContainerNamedVolume ToNamedVolume(NamedVolume namedVolume)
	{
		return new ContainerNamedVolume(namedVolume.Name, namedVolume.ContainerPath, namedVolume.ReadOnly);
	}
}
