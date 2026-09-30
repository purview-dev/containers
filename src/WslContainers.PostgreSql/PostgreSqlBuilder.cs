namespace Purview.WslContainers.PostgreSql;

/// <summary>Fluent builder for a PostgreSQL test container.</summary>
public class PostgreSqlBuilder : ContainerBuilder<PostgreSqlBuilder, PostgreSqlContainer, PostgreSqlConfiguration>
{
	/// <summary>Default PostgreSQL port.</summary>
	public const ushort PostgreSqlPort = 5432;

	/// <summary>Default image.</summary>
	public const string PostgreSqlImage = "postgres:17";

	private string database = "postgres";
	private string username = "postgres";
	private Secret password = Secret.From("postgres");

	/// <summary>Creates a builder with the default image.</summary>
	public PostgreSqlBuilder()
		: this(PostgreSqlImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public PostgreSqlBuilder(string image)
	{
		WithImage(image).WithPortBinding(PostgreSqlPort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public PostgreSqlBuilder(IContainerRuntime runtime)
		: base(runtime)
	{
		WithImage(PostgreSqlImage).WithPortBinding(PostgreSqlPort, assignRandomHostPort: true);
	}

	/// <summary>Sets the database name.</summary>
	public PostgreSqlBuilder WithDatabase(string database)
	{
		this.database = database;
		WithEnvironment("POSTGRES_DB", database);
		return this;
	}

	/// <summary>Sets the superuser name.</summary>
	public PostgreSqlBuilder WithUsername(string username)
	{
		this.username = username;
		WithEnvironment("POSTGRES_USER", username);
		return this;
	}

	/// <summary>Sets the superuser password.</summary>
	public PostgreSqlBuilder WithPassword(string password)
	{
		this.password = Secret.From(password);
		WithEnvironment("POSTGRES_PASSWORD", password);
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal PostgreSqlConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override PostgreSqlConfiguration BuildConfiguration()
	{
		PostgreSqlConfiguration configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new Dictionary<string, string>(
			configuration.Environment,
			StringComparer.Ordinal
		)
		{
			["POSTGRES_DB"] = database,
			["POSTGRES_USER"] = username,
			["POSTGRES_PASSWORD"] = password.Value,
		};
		IReadOnlyList<IWaitStrategy> waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForCommand("pg_isready", "-U", username, "-d", database) };
		return configuration with
		{
			Database = database,
			Username = username,
			Password = password,
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
			throw new WslContainerConfigurationException("PostgreSQL database cannot be empty.");
		}

		if (string.IsNullOrWhiteSpace(username))
		{
			throw new WslContainerConfigurationException("PostgreSQL username cannot be empty.");
		}

		if (string.IsNullOrEmpty(password.Value))
		{
			throw new WslContainerConfigurationException("PostgreSQL password cannot be empty.");
		}
	}

	/// <inheritdoc />
	protected override PostgreSqlContainer CreateContainer(PostgreSqlConfiguration configuration)
	{
		return new PostgreSqlContainer(configuration, Runtime ?? WslContainerRuntime.Instance);
	}
}
