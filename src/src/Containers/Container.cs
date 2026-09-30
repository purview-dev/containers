namespace Purview.Containers;

/// <summary>
/// The generic, backend-agnostic container produced by <see cref="ContainerBuilder" />. The backend is
/// resolved when the container starts, so a builder can be configured without knowing which runtime will
/// run it.
/// </summary>
public class Container : ContainerBase
{
	/// <summary>Creates a container for the configuration, optionally bound to a specific backend.</summary>
	public Container(IContainerConfiguration configuration, IContainerBackend? backend = null)
		: base(configuration, backend) { }
}
