using Purview.WslContainers.Images;
using Purview.WslContainers.Mounts;
using Purview.WslContainers.Networking;
using Purview.WslContainers.Runtime;
using Purview.WslContainers.Waiting;

namespace Purview.WslContainers;

/// <summary>
/// Fluent builder base for containers and strongly typed service modules.
/// Builder instances accumulate configuration; <see cref="Build" /> produces an immutable configuration
/// and a container bound to a runtime.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1005:Avoid excessive parameters on generic types")]
public abstract class ContainerBuilder<TBuilder, TContainer, TConfiguration>
	where TBuilder : ContainerBuilder<TBuilder, TContainer, TConfiguration>
	where TContainer : WslContainer
	where TConfiguration : ContainerConfiguration, new()
{
	string? _image;
	Image? _parsedImage;
	string? _name;
	string? _hostname;
	string? _domainName;
	readonly Dictionary<string, string> _environment = [with(StringComparer.Ordinal)];
	readonly List<string> _command = [];
	string? _workingDirectory;
	readonly List<PortBinding> _portBindings = [];
	readonly List<BindMount> _bindMounts = [];
	readonly List<NamedVolume> _namedVolumes = [];
	ContainerNetworkingMode _networkingMode = ContainerNetworkingMode.Bridged;
	bool _privileged;
	bool _enableGPU;
	bool _enableAutoRemove;
	PullPolicy _pullPolicy = PullPolicy.Missing;
	TimeSpan _startupTimeout = TimeSpan.FromMinutes(5);
	readonly List<IWaitStrategy> _waitStrategies = [];
	RegistryCredentials? _registryCredentials;

	/// <summary>The runtime the built container will use. Defaults to the process-wide <see cref="WslContainerRuntime.Instance" />.</summary>
	protected IContainerRuntime? Runtime { get; private set; }

	/// <summary>Creates a builder using the default process-wide runtime.</summary>
	protected ContainerBuilder() { }

	/// <summary>Creates a builder using an explicit runtime.</summary>
	protected ContainerBuilder(IContainerRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		Runtime = runtime;
	}

	/// <summary>Sets the container image. Invalid references fail fast (at configuration time).</summary>
	public TBuilder WithImage(string image)
	{
		try
		{
			_parsedImage = Image.Parse(image);
			_image = _parsedImage.Value.FullReference;
		}
		catch (ArgumentException ex)
		{
			throw new WslContainerConfigurationException(ex.Message, ex);
		}

		return (TBuilder)this;
	}

	/// <summary>Sets the container image from a parsed <see cref="Image" /> reference.</summary>
	public TBuilder WithImage(Image image)
	{
		_parsedImage = image;
		_image = image.FullReference;
		return (TBuilder)this;
	}

	/// <summary>Applies a tag to the configured image. Requires <see cref="WithImage(string)" /> to have been called.</summary>
	public TBuilder WithTag(string tag)
	{
		if (_parsedImage is not Image current)
		{
			throw new WslContainerConfigurationException("Set an image with WithImage(...) before applying a tag.");
		}

		try
		{
			_parsedImage = current.WithTag(tag);
			_image = _parsedImage.Value.FullReference;
		}
		catch (ArgumentException ex)
		{
			throw new WslContainerConfigurationException(ex.Message, ex);
		}

		return (TBuilder)this;
	}

	/// <summary>Sets a fixed container name. When unset, a unique name is generated.</summary>
	public TBuilder WithName(string name)
	{
		_name = name;
		return (TBuilder)this;
	}

	/// <summary>Sets the container hostname.</summary>
	public TBuilder WithHostname(string hostname)
	{
		_hostname = hostname;
		return (TBuilder)this;
	}

	/// <summary>Sets the container domain name.</summary>
	public TBuilder WithDomainName(string domainName)
	{
		_domainName = domainName;
		return (TBuilder)this;
	}

	/// <summary>Adds an environment variable for the init process.</summary>
	public TBuilder WithEnvironment(string key, string value)
	{
		_environment[key] = value;
		return (TBuilder)this;
	}

	/// <summary>Adds environment variables for the init process.</summary>
	public TBuilder WithEnvironment(IReadOnlyDictionary<string, string> environment)
	{
		ArgumentNullException.ThrowIfNull(environment);
		foreach ((var key, var value) in environment)
		{
			_environment[key] = value;
		}

		return (TBuilder)this;
	}

	/// <summary>Sets the init-process command line (argv, no shell).</summary>
	public TBuilder WithCommand(params string[] command)
	{
		ArgumentNullException.ThrowIfNull(command);
		_command.Clear();
		_command.AddRange(command);
		return (TBuilder)this;
	}

	/// <summary>Sets the working directory for the init process.</summary>
	public TBuilder WithWorkingDirectory(string workingDirectory)
	{
		_workingDirectory = workingDirectory;
		return (TBuilder)this;
	}

	/// <summary>Adds a port binding with an explicitly chosen host port.</summary>
	public TBuilder WithPortBinding(
		ushort containerPort,
		ushort hostPort,
		PortProtocol protocol = PortProtocol.Tcp,
		string? hostAddress = null
	)
	{
		_portBindings.Add(new PortBinding(containerPort, hostPort, protocol, hostAddress));
		return (TBuilder)this;
	}

	/// <summary>
	/// Adds a port binding. When <paramref name="assignRandomHostPort" /> is <c>true</c>, a free host port is
	/// allocated natively by WSLC; otherwise no host mapping is created.
	/// </summary>
	public TBuilder WithPortBinding(
		ushort containerPort,
		bool assignRandomHostPort = true,
		PortProtocol protocol = PortProtocol.Tcp,
		string? hostAddress = null
	)
	{
		if (assignRandomHostPort)
		{
			_portBindings.Add(new PortBinding(containerPort, HostPort: null, protocol, hostAddress));
		}

		return (TBuilder)this;
	}

	/// <summary>Adds a fully specified port binding.</summary>
	public TBuilder WithPortBinding(PortBinding portBinding)
	{
		ArgumentNullException.ThrowIfNull(portBinding);
		_portBindings.Add(portBinding);
		return (TBuilder)this;
	}

	/// <summary>Adds a bind mount of a Windows directory into the container.</summary>
	public TBuilder WithBindMount(string hostPath, string containerPath, bool readOnly = false)
	{
		_bindMounts.Add(new BindMount(hostPath, containerPath, readOnly));
		return (TBuilder)this;
	}

	/// <summary>Adds a named volume mount (auto-provisioned session VHD).</summary>
	public TBuilder WithVolumeMount(string name, string containerPath, bool readOnly = false)
	{
		_namedVolumes.Add(new NamedVolume(name, containerPath, readOnly));
		return (TBuilder)this;
	}

	/// <summary>Sets the container networking mode. Defaults to <see cref="ContainerNetworkingMode.Bridged" />.</summary>
	public TBuilder WithNetworkMode(ContainerNetworkingMode networkingMode)
	{
		_networkingMode = networkingMode;
		return (TBuilder)this;
	}

	/// <summary>Runs the container in privileged mode.</summary>
	public TBuilder WithPrivileged(bool privileged = true)
	{
		_privileged = privileged;
		return (TBuilder)this;
	}

	/// <summary>Exposes GPU devices to the container.</summary>
	public TBuilder WithGPU(bool enable = true)
	{
		_enableGPU = enable;
		return (TBuilder)this;
	}

	/// <summary>
	/// Controls automatic container removal when the init process exits. Defaults to <c>false</c>: the library
	/// deletes the container itself on dispose, and auto-removal would delete one-shot containers before their
	/// state/output can be inspected.
	/// </summary>
	public TBuilder WithAutoRemove(bool enable = true)
	{
		_enableAutoRemove = enable;
		return (TBuilder)this;
	}

	/// <summary>Sets the image pull policy. Defaults to <see cref="PullPolicy.Missing" />.</summary>
	public TBuilder WithPullPolicy(PullPolicy pullPolicy)
	{
		_pullPolicy = pullPolicy;
		return (TBuilder)this;
	}

	/// <summary>Sets the maximum time for startup-related waits.</summary>
	public TBuilder WithStartupTimeout(TimeSpan startupTimeout)
	{
		_startupTimeout = startupTimeout;
		return (TBuilder)this;
	}

	/// <summary>Adds readiness strategies evaluated after the container starts.</summary>
	public TBuilder WithWaitStrategy(params IWaitStrategy[] strategies)
	{
		ArgumentNullException.ThrowIfNull(strategies);
		_waitStrategies.AddRange(strategies);
		return (TBuilder)this;
	}

	/// <summary>Sets private-registry credentials used when pulling the image.</summary>
	public TBuilder WithRegistryCredentials(RegistryCredentials credentials)
	{
		ArgumentNullException.ThrowIfNull(credentials);
		_registryCredentials = credentials;
		return (TBuilder)this;
	}

	/// <summary>Sets private-registry credentials used when pulling the image.</summary>
	public TBuilder WithRegistryCredentials(Uri serverAddress, string username, string password)
	{
		_registryCredentials = RegistryCredentials.From(serverAddress, username, password);
		return (TBuilder)this;
	}

	/// <summary>Sets the runtime the built container binds to.</summary>
	public TBuilder WithRuntime(IContainerRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		Runtime = runtime;
		return (TBuilder)this;
	}

	/// <summary>Validates the configuration and builds the container.</summary>
	public virtual TContainer Build()
	{
		var configuration = BuildConfiguration();
		Validate(configuration);
		return CreateContainer(configuration);
	}

	/// <summary>Builds the immutable configuration from the accumulated builder state.</summary>
	protected virtual TConfiguration BuildConfiguration()
	{
		return new TConfiguration
		{
			Image =
				_image
				?? throw new WslContainerConfigurationException(
					"An image is required. Call WithImage(...) before Build()."
				),
			Name = _name,
			Hostname = _hostname,
			DomainName = _domainName,
			Environment = _environment,
			Command = _command,
			WorkingDirectory = _workingDirectory,
			PortBindings = _portBindings,
			BindMounts = _bindMounts,
			NamedVolumes = _namedVolumes,
			NetworkingMode = _networkingMode,
			Privileged = _privileged,
			EnableGPU = _enableGPU,
			EnableAutoRemove = _enableAutoRemove,
			PullPolicy = _pullPolicy,
			StartupTimeout = _startupTimeout,
			WaitStrategies = _waitStrategies,
			RegistryCredentials = _registryCredentials,
		};
	}

	/// <summary>Performs configuration validation before build completes.</summary>
	protected virtual void Validate(ContainerConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		if (string.IsNullOrWhiteSpace(configuration.Image))
		{
			throw new WslContainerConfigurationException("An image is required.");
		}

		if (configuration.StartupTimeout <= TimeSpan.Zero)
		{
			throw new WslContainerConfigurationException(
				$"Startup timeout must be positive; got {configuration.StartupTimeout}."
			);
		}

		foreach (var portBinding in configuration.PortBindings)
		{
			if (portBinding.Protocol == PortProtocol.Udp)
			{
				throw new WslContainerNotSupportedException(
					"UDP port mappings are not supported by the WSLC managed API."
				);
			}

			if (portBinding.ContainerPort == 0)
			{
				throw new WslContainerConfigurationException("Container port 0 is invalid for a port binding.");
			}

			if (portBinding.HostPort == 0)
			{
				throw new WslContainerConfigurationException("Host port 0 is invalid; use a random host port instead.");
			}
		}

		foreach (var bindMount in configuration.BindMounts)
		{
			if (string.IsNullOrWhiteSpace(bindMount.HostPath) || string.IsNullOrWhiteSpace(bindMount.ContainerPath))
			{
				throw new WslContainerConfigurationException("Bind mounts require a host path and a container path.");
			}
		}

		foreach (var namedVolume in configuration.NamedVolumes)
		{
			if (string.IsNullOrWhiteSpace(namedVolume.Name) || string.IsNullOrWhiteSpace(namedVolume.ContainerPath))
			{
				throw new WslContainerConfigurationException(
					"Named volumes require a volume name and a container path."
				);
			}
		}
	}

	/// <summary>Creates the container object bound to a runtime.</summary>
	protected abstract TContainer CreateContainer(TConfiguration configuration);
}

/// <summary>Fluent builder for a generic container.</summary>
public class ContainerBuilder : ContainerBuilder<ContainerBuilder, WslContainer, ContainerConfiguration>
{
	/// <summary>Creates a builder for a generic container.</summary>
	public ContainerBuilder() { }

	/// <summary>Creates a builder for a generic container using an explicit runtime.</summary>
	public ContainerBuilder(IContainerRuntime runtime)
		: base(runtime) { }

	/// <inheritdoc />
	protected override WslContainer CreateContainer(ContainerConfiguration configuration)
	{
		var runtime = Runtime ?? WslContainerRuntime.Instance;
		return new WslContainer(configuration, runtime);
	}
}
