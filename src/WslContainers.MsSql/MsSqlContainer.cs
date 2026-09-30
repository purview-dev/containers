using Microsoft.Data.SqlClient;

namespace Purview.WslContainers.MsSql;

/// <summary>A throwaway Microsoft SQL Server instance running on WSL Containers.</summary>
public sealed class MsSqlContainer : WslContainer
{
	private readonly MsSqlConfiguration configuration;

	internal MsSqlContainer(MsSqlConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
	{
		this.configuration = configuration;
	}

	/// <summary>Connection string pointing at the mapped host port. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString()
	{
		SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
		{
			// 127.0.0.1 is required: WSLC maps IPv4 loopback only, and Microsoft.Data.SqlClient
			// hangs on the IPv6 ::1 address that 'localhost' resolves to.
			DataSource = $"127.0.0.1,{GetMappedPublicPort(MsSqlBuilder.MsSqlPort)}",
			UserID = "sa",
			Password = configuration.Password.Value,
			TrustServerCertificate = true,
		};
		return builder.ConnectionString;
	}
}
