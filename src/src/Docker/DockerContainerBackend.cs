using Docker.DotNet.Models;
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
	readonly Lazy<WslDockerHost?> _wslDockerHost = new(WslDockerHost.Detect);

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
		var name = ContainerName.Generate(configuration);
		return new DockerContainer(configuration, token => CreateHandleAsync(configuration, name, token), name);
	}

	/// <inheritdoc />
	public async Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(10));
		try
		{
			// Resolving the endpoint throws when no Docker environment can be discovered, and the ping
			// throws when the daemon is not running; both are reported as "unavailable" with the reason.
			var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;
			using var client = endpoint.GetDockerClientBuilder(Guid.NewGuid()).Build();
			await client.System.PingAsync(deadline.Token).ConfigureAwait(false);
			var info = await client.System.GetSystemInfoAsync(deadline.Token).ConfigureAwait(false);
			return new ContainerBackendInfo(
				Name,
				IsAvailable: true,
				IsCompatible: true,
				info.ServerVersion ?? string.Empty,
				[]
			);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
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

	/// <inheritdoc />
	public async Task<IContainerResource> CreateNetworkAsync(
		NetworkConfiguration? configuration = null,
		CancellationToken cancellationToken = default
	)
	{
		configuration ??= new();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(configuration.Timeout);
		deadline.Token.ThrowIfCancellationRequested();
		var name = configuration.Name ?? $"purview-{Guid.NewGuid():N}";
		var network = new DotNet.Testcontainers.Builders.NetworkBuilder().WithName(name).Build();
		try
		{
			await network.CreateAsync(deadline.Token).ConfigureAwait(false);
			return new DockerResource(name, network, network, configuration.Timeout);
		}
		catch
		{
			await network
				.DisposeAsync()
				.AsTask()
				.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None)
				.ConfigureAwait(false);
			throw;
		}
	}

	/// <inheritdoc />
	public async Task<IContainerResource> BuildImageAsync(
		ImageBuildConfiguration configuration,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(configuration.Timeout);
		deadline.Token.ThrowIfCancellationRequested();
		var builder = new DotNet.Testcontainers.Builders.ImageFromDockerfileBuilder()
			.WithName(configuration.Name)
			.WithDockerfileDirectory(configuration.ContextDirectory)
			.WithDockerfile(configuration.Dockerfile);
		if (configuration.Target is not null)
			builder = builder.WithTarget(configuration.Target);
		var image = builder.Build();
		try
		{
			await image.CreateAsync(deadline.Token).ConfigureAwait(false);
			return new DockerResource(configuration.Name, image, image, TimeSpan.FromSeconds(30));
		}
		catch
		{
			await image
				.DisposeAsync()
				.AsTask()
				.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None)
				.ConfigureAwait(false);
			throw;
		}
	}

	async Task<DotNet.Testcontainers.Containers.IContainer> CreateHandleAsync(
		IContainerConfiguration configuration,
		string name,
		CancellationToken cancellationToken
	)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (configuration.RegistryCredentials is { } credentials && configuration.PullPolicy != Images.PullPolicy.Never)
		{
			var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;
			using var client = endpoint.GetDockerClientBuilder(Guid.NewGuid()).Build();
			var pull = configuration.PullPolicy == Images.PullPolicy.Always;
			if (!pull)
			{
				try
				{
					await client.Images.InspectImageAsync(configuration.Image, cancellationToken).ConfigureAwait(false);
				}
				catch (global::Docker.DotNet.DockerImageNotFoundException)
				{
					pull = true;
				}
			}
			if (pull)
			{
				await client
					.Images.CreateImageAsync(
						new ImagesCreateParameters { FromImage = configuration.Image },
						new AuthConfig
						{
							ServerAddress = credentials.ServerAddress.ToString(),
							Username = credentials.Username,
							Password = credentials.Password.Value,
						},
						new Progress<JSONMessage>(),
						cancellationToken
					)
					.ConfigureAwait(false);
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		var container = CreateBuilder(configuration, name).Build();
		if (configuration.ResourceMappings.Count > 0)
		{
			// Testcontainers' created event is synchronous and precedes init startup. Its directory
			// mappings omit empty directories, so use our cancellable archive transport here.
			container.Created += (_, _) =>
			{
				foreach (var mapping in configuration.ResourceMappings)
					DockerFileCopy
						.CopyAsync(container.Id, mapping.Source, mapping.Target, cancellationToken)
						.GetAwaiter()
						.GetResult();
			};
		}
		return container;
	}

	TcContainerBuilder CreateBuilder(IContainerConfiguration configuration, string name)
	{
		var builder = new TcContainerBuilder(configuration.Image)
			.WithName(name)
			.WithAutoRemove(configuration.EnableAutoRemove)
			.WithReuse(configuration.Reuse)
			.WithImagePullPolicy(
				(configuration.RegistryCredentials is null ? configuration.PullPolicy : Images.PullPolicy.Never) switch
				{
					Images.PullPolicy.Always => TcPullPolicy.Always,
					Images.PullPolicy.Never => TcPullPolicy.Never,
					Images.PullPolicy.Missing or _ => TcPullPolicy.Missing,
				}
			);

		builder = ApplyAdvancedConfiguration(builder, configuration);
		if (configuration.ResourceMappings.Count > 0)
			builder = builder.WithLabel(
				"purview.containers.resource-mappings",
				Convert.ToHexString(
					System.Security.Cryptography.SHA256.HashData(
						System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(configuration.ResourceMappings)
					)
				)
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
			var port = $"{binding.ContainerPort}/{(binding.Protocol == Networking.PortProtocol.Udp ? "udp" : "tcp")}";
			builder = binding.HostPort is ushort hostPort
				? builder.WithPortBinding(hostPort.ToString(System.Globalization.CultureInfo.InvariantCulture), port)
				: builder.WithPortBinding(port, assignRandomHostPort: true);
		}

		foreach (var mount in configuration.BindMounts)
		{
			var source =
				OperatingSystem.IsWindows() && WslDockerHost.IsWindowsDrivePath(mount.HostPath)
					? _wslDockerHost.Value?.Resolve(mount.HostPath) ?? mount.HostPath
					: mount.HostPath;
			builder = builder.WithBindMount(source, mount.ContainerPath, ToAccessMode(mount.ReadOnly));
		}

		foreach (var volume in configuration.NamedVolumes)
		{
			builder = builder.WithVolumeMount(volume.Name, volume.ContainerPath, ToAccessMode(volume.ReadOnly));
		}
		return builder;
	}

	static TcContainerBuilder ApplyAdvancedConfiguration(
		TcContainerBuilder builder,
		IContainerConfiguration configuration
	)
	{
		if (configuration.Entrypoint.Count > 0)
			builder = builder.WithEntrypoint([.. configuration.Entrypoint]);
		if (configuration.Labels.Count > 0)
			builder = builder.WithLabel(configuration.Labels);
		if (configuration.NetworkingMode == Networking.ContainerNetworkingMode.None)
			builder = builder.WithNetwork("none");
		else if (configuration.Network is not null)
			builder = builder.WithNetwork(configuration.Network);
		if (configuration.NetworkAliases.Count > 0)
			builder = builder.WithNetworkAliases(configuration.NetworkAliases);
		return builder.WithCreateParameterModifier(parameters =>
		{
			if (configuration.User is not null)
				parameters.User = configuration.User;
			if (configuration.DomainName is not null)
				parameters.Domainname = configuration.DomainName;
			parameters.StopTimeout = TimeSpan.FromSeconds(
				Math.Max(1, Math.Min(10, configuration.StopTimeout.TotalSeconds * 2 / 3))
			);
			parameters.HostConfig ??= new();
			if (configuration.EnableGPU)
				parameters.HostConfig.DeviceRequests =
				[
					new DeviceRequest { Count = -1, Capabilities = [new List<string> { "gpu" }] },
				];
			foreach (var binding in configuration.PortBindings)
			{
				var key =
					$"{binding.ContainerPort}/{(binding.Protocol == Networking.PortProtocol.Udp ? "udp" : "tcp")}";
				parameters.HostConfig.PortBindings ??=
					new Dictionary<string, IList<global::Docker.DotNet.Models.PortBinding>>();
				parameters.HostConfig.PortBindings[key] =
				[
					new global::Docker.DotNet.Models.PortBinding
					{
						HostIP = binding.HostAddress ?? "127.0.0.1",
						HostPort = binding.HostPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0",
					},
				];
			}
		});
	}

	static AccessMode ToAccessMode(bool readOnly) => readOnly ? AccessMode.ReadOnly : AccessMode.ReadWrite;
}
