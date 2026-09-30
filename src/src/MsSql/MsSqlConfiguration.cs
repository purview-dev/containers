using Purview.Containers.Diagnostics;

namespace Purview.Containers.MsSql;

/// <summary>Immutable configuration for a Microsoft SQL Server test container.</summary>
public sealed record MsSqlConfiguration : ContainerConfiguration
{
	/// <summary>SA password (MSSQL_SA_PASSWORD). Rendered as redacted.</summary>
	public Secret Password { get; init; } = Secret.From("YourStrong!Passw0rd");

	/// <summary>True when the SQL Server EULA has been explicitly accepted.</summary>
	public bool AcceptLicense { get; init; }
}
