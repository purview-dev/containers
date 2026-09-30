using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

/// <summary>
/// The WSL Containers (WSLC) backend: it creates containers on the process-wide WSLC session and reports
/// the installed WSL Containers components. No Docker installation is involved.
/// </summary>
public sealed class WslContainerBackend : IContainerBackend
{
	readonly IContainerRuntime? _runtime;

	/// <summary>Creates the backend over the process-wide <see cref="WslContainerRuntime.Instance" />.</summary>
	public WslContainerBackend() { }

	/// <summary>Creates the backend over an explicit runtime (isolation experiments and tests).</summary>
	public WslContainerBackend(IContainerRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		_runtime = runtime;
	}

	/// <summary>Stable backend identifier.</summary>
	public string Name => "wsl";

	/// <summary>Factory used by the generated backend registration.</summary>
	public static WslContainerBackend Create() => new();

	IContainerRuntime Runtime => _runtime ?? WslContainerRuntime.Instance;

	/// <inheritdoc />
	public IContainer CreateContainer(IContainerConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		if (configuration is not ContainerConfiguration wslc)
		{
			throw new ContainerConfigurationException(
				"The WSL Containers backend requires a ContainerConfiguration-derived configuration; "
					+ $"{configuration.GetType().Name} is not supported."
			);
		}

		foreach (var binding in configuration.PortBindings)
		{
			if (binding.Protocol == Networking.PortProtocol.Udp)
			{
				throw new ContainerNotSupportedException(
					"UDP port mappings are not supported by the WSL Containers managed API."
				);
			}
		}

		return new WslContainer(wslc, Runtime);
	}

	/// <inheritdoc />
	public async Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default)
	{
		var info = await Runtime.GetInfoAsync(cancellationToken).ConfigureAwait(false);
		return new ContainerBackendInfo(
			"wsl",
			info.IsAvailable,
			info.IsCompatible,
			info.Version,
			info.MissingComponents
		);
	}
}
