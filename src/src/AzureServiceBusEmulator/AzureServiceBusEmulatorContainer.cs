using Purview.Containers.Mounts;
using Purview.Containers.MsSql;
using Purview.Containers.Runtime;

namespace Purview.Containers.AzureServiceBusEmulator;

/// <summary>
/// A throwaway <see href="https://github.com/Azure/azure-service-bus-emulator-installer">Azure Service Bus
/// Emulator</see> instance running on WSL Containers or Docker, together with the SQL Server sidecar it
/// stores its state in. Starting the container starts the sidecar first; disposing it removes both.
/// </summary>
public sealed class AzureServiceBusEmulatorContainer : ContainerBase
{
	const string ConfigFileName = "Config.json";

	// The emulator's fixed development credentials: it accepts only this key name and value.
	const string SharedAccessKeyName = "RootManageSharedAccessKey";
	const string SharedAccessKey = "SAS_KEY_VALUE";

	readonly AzureServiceBusEmulatorConfiguration _configuration;

	int _prepared;
	string? _configDirectory;

	internal AzureServiceBusEmulatorContainer(
		AzureServiceBusEmulatorConfiguration configuration,
		MsSqlContainer sqlServer,
		IContainerBackend? backend
	)
		: base(configuration, backend)
	{
		_configuration = configuration;
		SqlServer = sqlServer;
	}

	/// <summary>The SQL Server sidecar the emulator stores its state in (for diagnostics such as its logs).</summary>
	public MsSqlContainer SqlServer { get; }

	/// <summary>
	/// Service Bus connection string for the AMQP endpoint, for <c>ServiceBusClient</c>. Safe to call after
	/// <see cref="IContainer.StartAsync" />.
	/// </summary>
	public string GetConnectionString() => BuildConnectionString(AzureServiceBusEmulatorBuilder.AmqpPort);

	/// <summary>
	/// Service Bus connection string for the management endpoint, for <c>ServiceBusAdministrationClient</c>.
	/// Safe to call after <see cref="IContainer.StartAsync" />.
	/// </summary>
	public string GetManagementConnectionString() =>
		BuildConnectionString(AzureServiceBusEmulatorBuilder.ManagementPort);

	/// <inheritdoc />
	public override async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (Interlocked.Exchange(ref _prepared, 1) == 1)
		{
			return;
		}

		// The emulator connects to SQL Server as soon as it starts, so the sidecar must be accepting
		// connections first. There is no container-name DNS, so the emulator reaches it by bridge IP.
		await SqlServer.StartAsync(cancellationToken).ConfigureAwait(false);
		if (SqlServer.NetworkIp is null)
		{
			throw new ContainerException(
				"The SQL Server sidecar has no bridge-network IP address, so the Service Bus emulator cannot reach it."
			);
		}

		if (_configuration.ConfigJson is { } configJson)
		{
			_configDirectory = Path.Combine(Path.GetTempPath(), "purview-servicebus-emulator", Name);
			Directory.CreateDirectory(_configDirectory);
			await File.WriteAllTextAsync(Path.Combine(_configDirectory, ConfigFileName), configJson, cancellationToken)
				.ConfigureAwait(false);
		}

		await base.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async ValueTask DisposeAsync()
	{
		await base.DisposeAsync().ConfigureAwait(false);
		await SqlServer.DisposeAsync().ConfigureAwait(false);

		if (_configDirectory is not null && Directory.Exists(_configDirectory))
		{
			Directory.Delete(_configDirectory, recursive: true);
		}

		_configDirectory = null;
	}

	/// <inheritdoc />
	protected override IContainerConfiguration ConfigureForStart(IContainerConfiguration configuration)
	{
		if (configuration is not ContainerConfiguration concrete)
		{
			return configuration;
		}

		Dictionary<string, string> environment = new(concrete.Environment, StringComparer.Ordinal)
		{
			["SQL_SERVER"] = SqlServer.NetworkIp!,
		};

		List<BindMount> bindMounts = [.. concrete.BindMounts];
		if (_configDirectory is not null)
		{
			bindMounts.Add(
				new BindMount(_configDirectory, AzureServiceBusEmulatorBuilder.ConfigMountPath, ReadOnly: true)
			);
		}

		return concrete with
		{
			Environment = environment,
			BindMounts = bindMounts,
		};
	}

	string BuildConnectionString(ushort containerPort) =>
		$"Endpoint=sb://127.0.0.1:{GetMappedPublicPort(containerPort)};SharedAccessKeyName={SharedAccessKeyName};SharedAccessKey={SharedAccessKey};UseDevelopmentEmulator=true;";
}
