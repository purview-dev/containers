namespace Purview.Containers;

/// <summary>
/// A container runtime provider: it creates containers and reports whether it is usable on this host.
/// Implemented by the backend packages (<c>Purview.Containers.Wsl</c>, <c>Purview.Containers.Docker</c>)
/// and selected through <see cref="ContainerBackends" />.
/// </summary>
public interface IContainerBackend
{
	/// <summary>Stable backend identifier, for example <c>wsl</c> or <c>docker</c>.</summary>
	string Name { get; }

	/// <summary>Creates (but does not start) a container for the given configuration.</summary>
	IContainer CreateContainer(IContainerConfiguration configuration);

	/// <summary>Reports availability, version and missing components for diagnostics and selection.</summary>
	Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default);

	/// <summary>Creates a bridge network. Dispose it after every attached container.</summary>
	Task<IContainerResource> CreateNetworkAsync(
		NetworkConfiguration? configuration = null,
		CancellationToken cancellationToken = default
	) => throw new Runtime.ContainerNotSupportedException("This backend does not support managed networks.");

	/// <summary>Builds an image from a host-side Dockerfile context. Dispose it after its containers.</summary>
	Task<IContainerResource> BuildImageAsync(
		ImageBuildConfiguration configuration,
		CancellationToken cancellationToken = default
	) => throw new Runtime.ContainerNotSupportedException("This backend does not support image builds.");
}
