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
}
