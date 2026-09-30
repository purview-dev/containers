using Microsoft.Data.SqlClient;

namespace Purview.WslContainers.MsSql;

/// <summary>Fluent builder for a Microsoft SQL Server test container.</summary>
public class MsSqlBuilder : ContainerBuilder<MsSqlBuilder, MsSqlContainer, MsSqlConfiguration>
{
	/// <summary>Default SQL Server port.</summary>
	public const ushort MsSqlPort = 1433;

	/// <summary>Default image.</summary>
	public const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-latest";

	private Secret password = Secret.From("YourStrong!Passw0rd");
	private bool acceptLicense;

	/// <summary>Creates a builder with the default image.</summary>
	public MsSqlBuilder()
		: this(MsSqlImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public MsSqlBuilder(string image)
	{
		WithImage(image).WithPortBinding(MsSqlPort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public MsSqlBuilder(IContainerRuntime runtime)
		: base(runtime)
	{
		WithImage(MsSqlImage).WithPortBinding(MsSqlPort, assignRandomHostPort: true);
	}

	/// <summary>Sets the SA password. Must satisfy SQL Server password complexity (at least 8 characters).</summary>
	public MsSqlBuilder WithPassword(string password)
	{
		this.password = Secret.From(password);
		WithEnvironment("MSSQL_SA_PASSWORD", password);
		return this;
	}

	/// <summary>
	/// Explicitly accepts the SQL Server EULA (ACCEPT_EULA=Y). Required before Build; the library never
	/// accepts licensing terms on the caller's behalf.
	/// </summary>
	public MsSqlBuilder AcceptLicense()
	{
		acceptLicense = true;
		WithEnvironment("ACCEPT_EULA", "Y");
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal MsSqlConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override MsSqlConfiguration BuildConfiguration()
	{
		MsSqlConfiguration configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new Dictionary<string, string>(
			configuration.Environment,
			StringComparer.Ordinal
		)
		{
			["MSSQL_SA_PASSWORD"] = password.Value,
			["MSSQL_PID"] = "Developer",
		};
		if (acceptLicense)
		{
			environment["ACCEPT_EULA"] = "Y";
		}

		IReadOnlyList<IWaitStrategy> waitStrategies =
			configuration.WaitStrategies.Count > 0 ? configuration.WaitStrategies : new[] { BuildReadinessWait() };
		return configuration with
		{
			Password = password,
			AcceptLicense = acceptLicense,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (!acceptLicense)
		{
			throw new WslContainerConfigurationException(
				"The SQL Server image requires accepting the EULA. Call AcceptLicense() explicitly before Build()."
			);
		}

		if (password.Value.Length < 8)
		{
			throw new WslContainerConfigurationException(
				"The SQL Server SA password must be at least 8 characters long."
			);
		}
	}

	/// <inheritdoc />
	protected override MsSqlContainer CreateContainer(MsSqlConfiguration configuration)
	{
		return new MsSqlContainer(configuration, Runtime ?? WslContainerRuntime.Instance);
	}

	private IWaitStrategy BuildReadinessWait()
	{
		// SQL Server is ready only when it accepts a real connection; a listening TCP port is not enough.
		return Wait.ForCustom(
				async (context, cancellationToken) =>
				{
					int? hostPort = context.GetHostPort(MsSqlPort);
					if (hostPort is null)
					{
						return false;
					}

					SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
					{
						// 127.0.0.1 is required: WSLC maps IPv4 loopback only, and Microsoft.Data.SqlClient
						// hangs on the IPv6 ::1 address that 'localhost' resolves to.
						DataSource = $"127.0.0.1,{hostPort}",
						UserID = "sa",
						Password = password.Value,
						TrustServerCertificate = true,
					};
					try
					{
						await using SqlConnection connection = new SqlConnection(builder.ConnectionString);
						await connection.OpenAsync(cancellationToken);
						return connection.State == System.Data.ConnectionState.Open;
					}
					catch (SqlException)
					{
						return false;
					}
					catch (InvalidOperationException)
					{
						return false;
					}
				}
			)
			.WithInterval(TimeSpan.FromSeconds(2));
	}
}
