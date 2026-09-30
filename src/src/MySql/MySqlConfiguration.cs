using Purview.WslContainers.Diagnostics;

namespace Purview.WslContainers.MySql;

/// <summary>Immutable configuration for a MySQL test container.</summary>
public sealed record MySqlConfiguration : ContainerConfiguration
{
	/// <summary>Database name (MYSQL_DATABASE).</summary>
	public string Database { get; init; } = "test";

	/// <summary>Application user name (MYSQL_USER).</summary>
	public string Username { get; init; } = "test";

	/// <summary>Application user password (MYSQL_PASSWORD). Rendered as redacted.</summary>
	public Secret Password { get; init; } = Secret.From("test");

	/// <summary>Root password (MYSQL_ROOT_PASSWORD). Rendered as redacted.</summary>
	public Secret RootPassword { get; init; } = Secret.From("test");
}
