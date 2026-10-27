using Purview.Containers.Diagnostics;
using Purview.Containers.MsSql;
using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.AzureServiceBusEmulator;

/// <summary>
/// Fluent builder for an <see href="https://github.com/Azure/azure-service-bus-emulator-installer">Azure
/// Service Bus Emulator</see> test container. The emulator stores its state in SQL Server, so the container
/// starts a SQL Server sidecar on the same backend first and points the emulator at its bridge address.
/// </summary>
public class AzureServiceBusEmulatorBuilder
	: ContainerBuilder<
		AzureServiceBusEmulatorBuilder,
		AzureServiceBusEmulatorContainer,
		AzureServiceBusEmulatorConfiguration
	>
{
	/// <summary>AMQP port the Service Bus clients connect to.</summary>
	public const ushort AmqpPort = 5672;

	/// <summary>HTTP management port: the health endpoint and the administration (entity management) API.</summary>
	public const ushort ManagementPort = 5300;

	/// <summary>Default image.</summary>
	public const string EmulatorImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";

	/// <summary>Container directory the emulator reads <c>Config.json</c> from.</summary>
	public const string ConfigMountPath = "/ServiceBus_Emulator/ConfigFiles";

	/// <summary>Default SA password of the SQL Server sidecar.</summary>
	public const string DefaultSqlServerPassword = "YourStrong!Passw0rd";

	/// <summary>
	/// Default SQL Server sidecar image: the MsSql module's default, SQL Server 2025. SQL Server 2022 does not
	/// start on WSLC (it cannot create its system directory as the non-root <c>mssql</c> user).
	/// </summary>
	public const string DefaultSqlServerImage = MsSqlBuilder.MsSqlImage;

	bool _acceptLicense;
	string _sqlServerImage = DefaultSqlServerImage;
	Secret _sqlServerPassword = Secret.From(DefaultSqlServerPassword);
	string? _configJson;

	/// <summary>Creates a builder with the default image.</summary>
	public AzureServiceBusEmulatorBuilder()
		: this(EmulatorImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public AzureServiceBusEmulatorBuilder(string image)
	{
		ApplyDefaults(image);
	}

	/// <summary>Creates a builder using an explicit backend. The SQL Server sidecar runs on it too.</summary>
	public AzureServiceBusEmulatorBuilder(IContainerBackend backend)
		: base(backend)
	{
		ApplyDefaults(EmulatorImage);
	}

	/// <summary>
	/// Explicitly accepts the Azure Service Bus Emulator EULA and the SQL Server EULA of its sidecar
	/// (ACCEPT_EULA=Y on both). Required before Build; the library never accepts licensing terms on the
	/// caller's behalf.
	/// </summary>
	public AzureServiceBusEmulatorBuilder AcceptLicense()
	{
		_acceptLicense = true;
		WithEnvironment("ACCEPT_EULA", "Y");
		return this;
	}

	/// <summary>Sets the SQL Server sidecar image (default <see cref="DefaultSqlServerImage" />).</summary>
	public AzureServiceBusEmulatorBuilder WithSqlServerImage(string image)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(image);
		_sqlServerImage = image;
		return this;
	}

	/// <summary>Sets the SQL Server sidecar SA password. Must satisfy SQL Server password complexity (at least 8 characters).</summary>
	public AzureServiceBusEmulatorBuilder WithSqlServerPassword(string password)
	{
		_sqlServerPassword = Secret.From(password);
		return this;
	}

	/// <summary>
	/// Uses <paramref name="json" /> as the emulator <c>Config.json</c>, declaring the namespace and its
	/// queues, topics and subscriptions. Without it the image's default configuration applies.
	/// </summary>
	public AzureServiceBusEmulatorBuilder WithConfig(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);
		_configJson = json;
		return this;
	}

	/// <summary>Reads the emulator <c>Config.json</c> from <paramref name="path" />; see <see cref="WithConfig" />.</summary>
	public AzureServiceBusEmulatorBuilder WithConfigFile(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (!File.Exists(path))
		{
			throw new ContainerConfigurationException(
				$"The Service Bus emulator configuration file '{path}' does not exist."
			);
		}

		return WithConfig(File.ReadAllText(path));
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal AzureServiceBusEmulatorConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override AzureServiceBusEmulatorConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new(configuration.Environment, StringComparer.Ordinal)
		{
			["MSSQL_SA_PASSWORD"] = _sqlServerPassword.Value,
			// The sidecar is already accepting connections when the emulator starts, so skip its built-in delay.
			["SQL_WAIT_INTERVAL"] = "0",
		};
		if (_acceptLicense)
		{
			environment["ACCEPT_EULA"] = "Y";
		}

		var waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)BuildDefaultWaitStrategy() };

		return configuration with
		{
			Environment = environment,
			WaitStrategies = waitStrategies,
			AcceptLicense = _acceptLicense,
			SqlServerImage = _sqlServerImage,
			SqlServerPassword = _sqlServerPassword,
			ConfigJson = _configJson,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (!_acceptLicense)
		{
			throw new ContainerConfigurationException(
				"The Azure Service Bus Emulator and its SQL Server sidecar require accepting their EULAs. Call AcceptLicense() explicitly before Build()."
			);
		}

		if (_sqlServerPassword.Value.Length < 8)
		{
			throw new ContainerConfigurationException("The SQL Server SA password must be at least 8 characters long.");
		}
	}

	/// <inheritdoc />
	protected override AzureServiceBusEmulatorContainer CreateContainer(
		AzureServiceBusEmulatorConfiguration configuration
	)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		return new(configuration, BuildSqlServer(configuration), Backend);
	}

	MsSqlContainer BuildSqlServer(AzureServiceBusEmulatorConfiguration configuration)
	{
		var builder = Backend is null ? new MsSqlBuilder() : new MsSqlBuilder(Backend);
		return builder
			.WithImage(configuration.SqlServerImage)
			.WithPassword(configuration.SqlServerPassword.Value)
			.AcceptLicense()
			.WithStartupTimeout(configuration.StartupTimeout)
			.Build();
	}

	static WaitStrategy BuildDefaultWaitStrategy()
	{
		// The emulator's own health endpoint proves the service is up, not merely that a port is listening.
		return Wait.ForHttp("/health").ForPort(ManagementPort).WithInterval(TimeSpan.FromSeconds(1));
	}

	void ApplyDefaults(string image)
	{
		WithImage(image)
			.WithPortBinding(AmqpPort, assignRandomHostPort: true)
			.WithPortBinding(ManagementPort, assignRandomHostPort: true)
			.WithConnectionStringProvider(new AzureServiceBusEmulatorConnectionStringProvider());
	}
}
