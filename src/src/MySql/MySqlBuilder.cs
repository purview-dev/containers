using MySqlConnector;
using Purview.Containers.Diagnostics;
using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.MySql;

/// <summary>Fluent builder for a MySQL test container.</summary>
public class MySqlBuilder : ContainerBuilder<MySqlBuilder, MySqlContainer, MySqlConfiguration>
{
	/// <summary>Default MySQL port.</summary>
	public const ushort MySqlPort = 3306;

	/// <summary>Default image.</summary>
	public const string MySqlImage = "mysql:8";

	string _database = "test";
	string _username = "test";
	Secret _password = Secret.From("test");
	Secret _rootPassword = Secret.From("test");

	/// <summary>Creates a builder with the default image.</summary>
	public MySqlBuilder()
		: this(MySqlImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public MySqlBuilder(string image)
	{
		WithImage(image).WithPortBinding(MySqlPort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public MySqlBuilder(IContainerBackend backend)
		: base(backend)
	{
		WithImage(MySqlImage).WithPortBinding(MySqlPort, assignRandomHostPort: true);
	}

	/// <summary>Sets the database name.</summary>
	public MySqlBuilder WithDatabase(string database)
	{
		_database = database;
		WithEnvironment("MYSQL_DATABASE", database);
		return this;
	}

	/// <summary>Sets the application user name.</summary>
	public MySqlBuilder WithUsername(string username)
	{
		_username = username;
		WithEnvironment("MYSQL_USER", username);
		return this;
	}

	/// <summary>Sets the application user password.</summary>
	public MySqlBuilder WithPassword(string password)
	{
		_password = Secret.From(password);
		WithEnvironment("MYSQL_PASSWORD", password);
		return this;
	}

	/// <summary>Sets the root password.</summary>
	public MySqlBuilder WithRootPassword(string rootPassword)
	{
		_rootPassword = Secret.From(rootPassword);
		WithEnvironment("MYSQL_ROOT_PASSWORD", rootPassword);
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal MySqlConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override MySqlConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new(configuration.Environment, StringComparer.Ordinal)
		{
			["MYSQL_DATABASE"] = _database,
			["MYSQL_USER"] = _username,
			["MYSQL_PASSWORD"] = _password.Value,
			["MYSQL_ROOT_PASSWORD"] = _rootPassword.Value,
		};
		var waitStrategies =
			configuration.WaitStrategies.Count > 0 ? configuration.WaitStrategies : new[] { BuildReadinessWait() };
		return configuration with
		{
			Database = _database,
			Username = _username,
			Password = _password,
			RootPassword = _rootPassword,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (string.IsNullOrWhiteSpace(_database))
		{
			throw new ContainerConfigurationException("MySQL database cannot be empty.");
		}

		if (string.IsNullOrEmpty(_password.Value))
		{
			throw new ContainerConfigurationException("MySQL password cannot be empty.");
		}
	}

	/// <inheritdoc />
	protected override MySqlContainer CreateContainer(MySqlConfiguration configuration)
	{
		return new MySqlContainer(configuration, Backend);
	}

	WaitStrategy BuildReadinessWait()
	{
		// The image logs "ready for connections" during its temporary init server, so the readiness
		// check must be a real host-side connection rather than a log match.
		return Wait.ForCustom(
				async (context, cancellationToken) =>
				{
					var hostPort = context.GetHostPort(MySqlPort);
					if (hostPort is null)
					{
						return false;
					}

					MySqlConnectionStringBuilder builder = new()
					{
						Server = "127.0.0.1",
						Port = (uint)hostPort.Value,
						Database = _database,
						UserID = _username,
						Password = _password.Value,
					};
					try
					{
						await using MySqlConnection connection = new(builder.ConnectionString);
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
