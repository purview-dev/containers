using DotNet.Testcontainers.Configurations;
using Purview.Containers.Runtime;

namespace Purview.Containers.Docker;

/// <summary>
/// The single place the Docker backend resolves Testcontainers' daemon endpoint. Testcontainers returns
/// <see langword="null" /> (rather than throwing) when none of its discovery providers finds a daemon,
/// so every caller would otherwise fail with a bare <see cref="NullReferenceException" />; this turns that
/// into a <see cref="ContainerBackendUnavailableException" /> that says what to fix.
/// </summary>
static class DockerEndpoint
{
	internal const string NotFoundMessage =
		"No Docker daemon was found. Start a Docker-compatible engine (Docker Desktop, Docker Engine, "
		+ "Rancher Desktop or Podman), or point DOCKER_HOST (or docker.host in ~/.testcontainers.properties) "
		+ "at a running daemon.";

	/// <summary>Resolves the daemon endpoint, or throws a <see cref="ContainerBackendUnavailableException" />.</summary>
	internal static IDockerEndpointAuthenticationConfiguration Resolve() =>
		TryResolve() ?? throw new ContainerBackendUnavailableException(NotFoundMessage);

	/// <summary>Resolves the daemon endpoint, or returns <see langword="null" /> when none was discovered.</summary>
	internal static IDockerEndpointAuthenticationConfiguration? TryResolve() =>
		TestcontainersSettings.OS?.DockerEndpointAuthConfig;

	/// <summary>Creates a Docker client for the resolved endpoint.</summary>
	internal static global::Docker.DotNet.DockerClient CreateClient() => CreateClient(Resolve());

	/// <summary>Creates a Docker client for <paramref name="endpoint" />.</summary>
	internal static global::Docker.DotNet.DockerClient CreateClient(
		IDockerEndpointAuthenticationConfiguration endpoint
	) => endpoint.GetDockerClientBuilder(Guid.NewGuid()).Build();
}
