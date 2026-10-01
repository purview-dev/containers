using Purview.Containers.Diagnostics;

namespace Purview.Containers;

/// <summary>Credentials for authenticating to a private container registry.</summary>
public sealed record RegistryCredentials(Uri ServerAddress, string Username, Secret Password)
{
	/// <summary>Creates credentials from a server URI string and plain password.</summary>
	public RegistryCredentials(string serverAddress, string username, string password)
		: this(new Uri(serverAddress), username, Secret.From(password)) { }

	/// <summary>Creates credentials from a plain password.</summary>
	public static RegistryCredentials From(Uri serverAddress, string username, string password)
	{
		return new RegistryCredentials(serverAddress, username, Secret.From(password));
	}
}
