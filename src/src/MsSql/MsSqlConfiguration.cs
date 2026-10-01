using Purview.Containers.Diagnostics;

namespace Purview.Containers.MsSql;

/// <summary>Immutable configuration for a Microsoft SQL Server test container.</summary>
public sealed record MsSqlConfiguration : ContainerConfiguration
{
	/// <summary>SA password (MSSQL_SA_PASSWORD). Rendered as redacted.</summary>
	public Secret Password { get; init; } = Secret.From("YourStrong!Passw0rd");

	/// <summary>
	/// Initial catalog the generated connection string points at (default <c>master</c>, matching the
	/// Testcontainers SQL Server module). Set with <see cref="MsSqlBuilder.WithDatabase" />.
	/// </summary>
	public string Database { get; init; } = "master";

	/// <summary>True when the SQL Server EULA has been explicitly accepted.</summary>
	public bool AcceptLicense { get; init; }
}
