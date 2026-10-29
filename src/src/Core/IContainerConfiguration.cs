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

	/// <summary>Override the image entrypoint; an empty list retains the image default.</summary>
	IReadOnlyList<string> Entrypoint => Array.Empty<string>();

	/// <summary>Linux user or uid[:gid]. Null retains the image default.</summary>
	string? User => null;

	/// <summary>Container metadata. Never put secrets in labels.</summary>
	IReadOnlyDictionary<string, string> Labels => new Dictionary<string, string>();

	/// <summary>Name of a previously created network, or null for the default bridge.</summary>
	string? Network => null;

	/// <summary>DNS aliases on the explicitly selected network.</summary>
	IReadOnlyList<string> NetworkAliases => Array.Empty<string>();

	/// <summary>Keep the container on dispose for reuse. WSLC reuse ends with the owning session.</summary>
	bool Reuse => false;

	/// <summary>Files/directories copied before the init process starts.</summary>
	IReadOnlyList<ContainerResourceMapping> ResourceMappings => Array.Empty<ContainerResourceMapping>();

	/// <summary>Default exec and init-exit wait deadline.</summary>
	TimeSpan ExecTimeout => TimeSpan.FromMinutes(1);

	/// <summary>Total stop budget, including force-kill escalation.</summary>
	TimeSpan StopTimeout => TimeSpan.FromSeconds(15);

	/// <summary>Total managed disposal budget.</summary>
	TimeSpan CleanupTimeout => TimeSpan.FromSeconds(30);

	/// <summary>Deadline for file operations and log snapshots (not live log tails).</summary>
	TimeSpan OperationTimeout => TimeSpan.FromMinutes(1);
}
