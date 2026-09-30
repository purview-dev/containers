using Microsoft.Data.SqlClient;
using Purview.Containers.Diagnostics;
using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.MsSql;

/// <summary>Fluent builder for a Microsoft SQL Server test container.</summary>
public class MsSqlBuilder : ContainerBuilder<MsSqlBuilder, MsSqlContainer, MsSqlConfiguration>
{
	/// <summary>Default SQL Server port.</summary>
	public const ushort MsSqlPort = 1433;

	/// <summary>Default image.</summary>
	public const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-latest";

	Secret _password = Secret.From("YourStrong!Passw0rd");
	bool _acceptLicense;

	/// <summary>Creates a builder with the default image.</summary>
	public MsSqlBuilder()
		: this(MsSqlImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public MsSqlBuilder(string image)
	{
		WithImage(image).WithPortBinding(MsSqlPort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public MsSqlBuilder(IContainerBackend backend)
		: base(backend)
	{
		WithImage(MsSqlImage).WithPortBinding(MsSqlPort, assignRandomHostPort: true);
	}

	/// <summary>Sets the SA password. Must satisfy SQL Server password complexity (at least 8 characters).</summary>
	public MsSqlBuilder WithPassword(string password)
	{
		_password = Secret.From(password);
		WithEnvironment("MSSQL_SA_PASSWORD", password);
		return this;
	}

	/// <summary>
	/// Explicitly accepts the SQL Server EULA (ACCEPT_EULA=Y). Required before Build; the library never
	/// accepts licensing terms on the caller's behalf.
	/// </summary>
	public MsSqlBuilder AcceptLicense()
	{
		_acceptLicense = true;
		WithEnvironment("ACCEPT_EULA", "Y");
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal MsSqlConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override MsSqlConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new(configuration.Environment, StringComparer.Ordinal)
		{
			["MSSQL_SA_PASSWORD"] = _password.Value,
			["MSSQL_PID"] = "Developer",
		};
		if (_acceptLicense)
		{
			environment["ACCEPT_EULA"] = "Y";
		}

		var waitStrategies =
			configuration.WaitStrategies.Count > 0 ? configuration.WaitStrategies : new[] { BuildReadinessWait() };
		return configuration with
		{
			Password = _password,
			AcceptLicense = _acceptLicense,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (!_acceptLicense)
		{
			throw new ContainerConfigurationException(
				"The SQL Server image requires accepting the EULA. Call AcceptLicense() explicitly before Build()."
			);
		}

		if (_password.Value.Length < 8)
		{
			throw new ContainerConfigurationException("The SQL Server SA password must be at least 8 characters long.");
		}
	}

	/// <inheritdoc />
	protected override MsSqlContainer CreateContainer(MsSqlConfiguration configuration)
	{
		return new MsSqlContainer(configuration, Backend);
	}

	WaitStrategy BuildReadinessWait()
	{
		// SQL Server is ready only when it accepts a real connection; a listening TCP port is not enough.
		return Wait.ForCustom(
				async (context, cancellationToken) =>
				{
					var hostPort = context.GetHostPort(MsSqlPort);
					if (hostPort is null)
					{
						return false;
					}

					SqlConnectionStringBuilder builder = new()
					{
						// 127.0.0.1 is required: WSLC maps IPv4 loopback only, and Microsoft.Data.SqlClient
						// hangs on the IPv6 ::1 address that 'localhost' resolves to.
						DataSource = $"127.0.0.1,{hostPort}",
						UserID = "sa",
						Password = _password.Value,
						TrustServerCertificate = true,
					};
					try
					{
						await using SqlConnection connection = new(builder.ConnectionString);
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
