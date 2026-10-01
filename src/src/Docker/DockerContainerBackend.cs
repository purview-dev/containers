using DotNet.Testcontainers.Configurations;
using TcContainerBuilder = DotNet.Testcontainers.Builders.ContainerBuilder;
using TcPullPolicy = DotNet.Testcontainers.Images.PullPolicy;

namespace Purview.Containers.Docker;

/// <summary>
/// The Docker backend: containers on any Docker daemon reachable from the test host — Docker Desktop,
/// Docker Engine inside WSL2, a remote daemon, or the daemon a CI runner provides. It drives the daemon
/// through Testcontainers, so the resource reaper (Ryuk) still cleans up after a crashed test host.
/// </summary>
public sealed class DockerContainerBackend : IContainerBackend, IContainerBackendPreference
{
	/// <summary>Stable backend identifier.</summary>
	public string Name => "docker";

	/// <summary>
	/// Automatic selection preference: a higher value than WSLC, so a machine that can run both prefers
	/// WSL Containers.
	/// </summary>
	public int AutoPriority => 100;

	/// <summary>Factory used by the generated backend registration.</summary>
	public static DockerContainerBackend Create() => new();

	/// <inheritdoc />
	public IContainer CreateContainer(IContainerConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		var container = CreateBuilder(configuration).Build();
		return new DockerContainer(configuration, container, ContainerName.Generate(configuration));
	}

	/// <inheritdoc />
	public async Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			// Resolving the endpoint throws when no Docker environment can be discovered, and the ping
			// throws when the daemon is not running; both are reported as "unavailable" with the reason.
			var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;
			using var client = endpoint.GetDockerClientBuilder(Guid.NewGuid()).Build();
			await client.System.PingAsync(cancellationToken).ConfigureAwait(false);
			var info = await client.System.GetSystemInfoAsync(cancellationToken).ConfigureAwait(false);
			return new ContainerBackendInfo(
				Name,
				IsAvailable: true,
				IsCompatible: true,
				info.ServerVersion ?? string.Empty,
				[]
			);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return new ContainerBackendInfo(
				Name,
				IsAvailable: false,
				IsCompatible: false,
				Version: string.Empty,
				MissingComponents: [$"{exception.GetType().Name}: {exception.Message}"]
			);
		}
	}

	static TcContainerBuilder CreateBuilder(IContainerConfiguration configuration)
	{
		var builder = new TcContainerBuilder(configuration.Image)
			.WithAutoRemove(configuration.EnableAutoRemove)
			.WithImagePullPolicy(
				configuration.PullPolicy switch
				{
					Images.PullPolicy.Always => TcPullPolicy.Always,
					Images.PullPolicy.Never => TcPullPolicy.Never,
					Images.PullPolicy.Missing or _ => TcPullPolicy.Missing,
				}
			);

		if (!string.IsNullOrWhiteSpace(configuration.Name))
		{
			builder = builder.WithName(configuration.Name);
		}

		if (!string.IsNullOrWhiteSpace(configuration.Hostname))
		{
			builder = builder.WithHostname(configuration.Hostname);
		}

		if (!string.IsNullOrWhiteSpace(configuration.WorkingDirectory))
		{
			builder = builder.WithWorkingDirectory(configuration.WorkingDirectory);
		}

		if (configuration.Command.Count > 0)
		{
			builder = builder.WithCommand([.. configuration.Command]);
		}

		if (configuration.Environment.Count > 0)
		{
			builder = builder.WithEnvironment(configuration.Environment);
		}

		if (configuration.Privileged)
		{
			builder = builder.WithPrivileged(true);
		}

		foreach (var binding in configuration.PortBindings)
		{
			builder = binding.HostPort is ushort hostPort
				? builder.WithPortBinding(hostPort, binding.ContainerPort)
				: builder.WithPortBinding(binding.ContainerPort, assignRandomHostPort: true);
		}

		foreach (var mount in configuration.BindMounts)
		{
			builder = builder.WithBindMount(mount.HostPath, mount.ContainerPath, ToAccessMode(mount.ReadOnly));
		}

		foreach (var volume in configuration.NamedVolumes)
		{
			builder = builder.WithVolumeMount(volume.Name, volume.ContainerPath, ToAccessMode(volume.ReadOnly));
		}

		return builder;
	}

	static AccessMode ToAccessMode(bool readOnly) => readOnly ? AccessMode.ReadOnly : AccessMode.ReadWrite;
}
