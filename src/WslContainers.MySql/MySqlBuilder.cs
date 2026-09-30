using MySqlConnector;

namespace Purview.WslContainers.MySql;

/// <summary>Fluent builder for a MySQL test container.</summary>
public class MySqlBuilder : ContainerBuilder<MySqlBuilder, MySqlContainer, MySqlConfiguration>
{
	/// <summary>Default MySQL port.</summary>
	public const ushort MySqlPort = 3306;

	/// <summary>Default image.</summary>
	public const string MySqlImage = "mysql:8";

	private string database = "test";
	private string username = "test";
	private Secret password = Secret.From("test");
	private Secret rootPassword = Secret.From("test");

	/// <summary>Creates a builder with the default image.</summary>
	public MySqlBuilder()
		: this(MySqlImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public MySqlBuilder(string image)
	{
		WithImage(image).WithPortBinding(MySqlPort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public MySqlBuilder(IContainerRuntime runtime)
		: base(runtime)
	{
		WithImage(MySqlImage).WithPortBinding(MySqlPort, assignRandomHostPort: true);
	}

	/// <summary>Sets the database name.</summary>
	public MySqlBuilder WithDatabase(string database)
	{
		this.database = database;
		WithEnvironment("MYSQL_DATABASE", database);
		return this;
	}

	/// <summary>Sets the application user name.</summary>
	public MySqlBuilder WithUsername(string username)
	{
		this.username = username;
		WithEnvironment("MYSQL_USER", username);
		return this;
	}

	/// <summary>Sets the application user password.</summary>
	public MySqlBuilder WithPassword(string password)
	{
		this.password = Secret.From(password);
		WithEnvironment("MYSQL_PASSWORD", password);
		return this;
	}

	/// <summary>Sets the root password.</summary>
	public MySqlBuilder WithRootPassword(string rootPassword)
	{
		this.rootPassword = Secret.From(rootPassword);
		WithEnvironment("MYSQL_ROOT_PASSWORD", rootPassword);
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal MySqlConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override MySqlConfiguration BuildConfiguration()
	{
		MySqlConfiguration configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new Dictionary<string, string>(
			configuration.Environment,
			StringComparer.Ordinal
		)
		{
			["MYSQL_DATABASE"] = database,
			["MYSQL_USER"] = username,
			["MYSQL_PASSWORD"] = password.Value,
			["MYSQL_ROOT_PASSWORD"] = rootPassword.Value,
		};
		IReadOnlyList<IWaitStrategy> waitStrategies =
			configuration.WaitStrategies.Count > 0 ? configuration.WaitStrategies : new[] { BuildReadinessWait() };
		return configuration with
		{
			Database = database,
			Username = username,
			Password = password,
			RootPassword = rootPassword,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (string.IsNullOrWhiteSpace(database))
		{
			throw new WslContainerConfigurationException("MySQL database cannot be empty.");
		}

		if (string.IsNullOrEmpty(password.Value))
		{
			throw new WslContainerConfigurationException("MySQL password cannot be empty.");
		}
	}

	/// <inheritdoc />
	protected override MySqlContainer CreateContainer(MySqlConfiguration configuration)
	{
		return new MySqlContainer(configuration, Runtime ?? WslContainerRuntime.Instance);
	}

	private IWaitStrategy BuildReadinessWait()
	{
		// The image logs "ready for connections" during its temporary init server, so the readiness
		// check must be a real host-side connection rather than a log match.
		return Wait.ForCustom(
				async (context, cancellationToken) =>
				{
					int? hostPort = context.GetHostPort(MySqlPort);
					if (hostPort is null)
					{
						return false;
					}

					MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
					{
						Server = "127.0.0.1",
						Port = (uint)hostPort.Value,
						Database = database,
						UserID = username,
						Password = password.Value,
					};
					try
					{
						await using MySqlConnection connection = new MySqlConnection(builder.ConnectionString);
						await connection.OpenAsync(cancellationToken);
						return connection.State == System.Data.ConnectionState.Open;
					}
					catch (MySqlException)
					{
						return false;
					}
				}
			)
			.WithInterval(TimeSpan.FromSeconds(1));
	}
}
