using Purview.Containers.Images;
using Purview.Containers.Mounts;
using Purview.Containers.Networking;
using Purview.Containers.Waiting;

namespace Purview.Containers;

/// <summary>Immutable container configuration consumed by the runtime at start time.</summary>
public interface IContainerConfiguration
{
	/// <summary>Image reference (registry/repository:tag).</summary>
	string Image { get; }

	/// <summary>Optional container name. When <c>null</c>, a unique name is generated.</summary>
	string? Name { get; }

	/// <summary>Optional container hostname.</summary>
	string? Hostname { get; }

	/// <summary>Optional container domain name.</summary>
	string? DomainName { get; }

	/// <summary>Environment variables for the init process.</summary>
	IReadOnlyDictionary<string, string> Environment { get; }

	/// <summary>Init-process command line (argv, no shell).</summary>
	IReadOnlyList<string> Command { get; }

	/// <summary>Working directory for the init process.</summary>
	string? WorkingDirectory { get; }

	/// <summary>Host-to-container port mappings.</summary>
	IReadOnlyList<PortBinding> PortBindings { get; }

	/// <summary>Windows bind mounts.</summary>
	IReadOnlyList<BindMount> BindMounts { get; }

	/// <summary>Named volume mounts.</summary>
	IReadOnlyList<NamedVolume> NamedVolumes { get; }

	/// <summary>Container networking mode. Defaults to <see cref="ContainerNetworkingMode.Bridged" />.</summary>
	ContainerNetworkingMode NetworkingMode { get; }

	/// <summary>Run the container in privileged mode.</summary>
	bool Privileged { get; }

	/// <summary>Expose GPU devices to the container.</summary>
	bool EnableGPU { get; }

	/// <summary>Automatically remove the container when it stops.</summary>
	bool EnableAutoRemove { get; }

	/// <summary>Image pull policy. Defaults to <see cref="PullPolicy.Missing" />.</summary>
	PullPolicy PullPolicy { get; }

	/// <summary>Maximum time for startup-related waits.</summary>
	TimeSpan StartupTimeout { get; }

	/// <summary>Readiness strategies evaluated after the container starts.</summary>
	IReadOnlyList<IWaitStrategy> WaitStrategies { get; }

	/// <summary>Optional private-registry credentials used when pulling the image.</summary>
	RegistryCredentials? RegistryCredentials { get; }
}
