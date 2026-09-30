using Purview.WslContainers.Diagnostics;

namespace Purview.WslContainers.PostgreSql;

/// <summary>Immutable configuration for a PostgreSQL test container.</summary>
public sealed record PostgreSqlConfiguration : ContainerConfiguration
{
	/// <summary>Database name (POSTGRES_DB).</summary>
	public string Database { get; init; } = "postgres";

	/// <summary>Superuser name (POSTGRES_USER).</summary>
	public string Username { get; init; } = "postgres";

	/// <summary>Superuser password (POSTGRES_PASSWORD). Rendered as redacted.</summary>
	public Secret Password { get; init; } = Secret.From("postgres");
}
