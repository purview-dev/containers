using System.Diagnostics.CodeAnalysis;
using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

/// <summary>
/// The WSL Containers (WSLC) backend: it creates containers on the process-wide WSLC session and reports
/// the installed WSL Containers components. No Docker installation is involved.
/// </summary>
public sealed class WslContainerBackend : IContainerBackend, IContainerBackendPreference
{
	/// <summary>Creates the backend over the process-wide <see cref="WslContainerRuntime.Instance" />.</summary>
	public WslContainerBackend() { }

	/// <summary>Creates the backend over an explicit runtime (isolation experiments and tests).</summary>
	public WslContainerBackend(IContainerRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		Runtime = runtime;
	}

	/// <summary>Creates the backend over a runtime configured with the given options.</summary>
	[SuppressMessage(
		"Reliability",
		"CA2000:Dispose objects before losing scope",
		Justification = "The runtime owns the process-wide session and is released by its process-exit hook; the backend is process-lifetime."
	)]
	public WslContainerBackend(WslContainerRuntimeOptions options)
		: this(new WslContainerRuntime(options)) { }

	/// <summary>Stable backend identifier.</summary>
	public string Name => "wsl";

	/// <summary>
	/// Automatic selection preference: WSLC is preferred over Docker on a machine that can run both.
	/// </summary>
	public int AutoPriority => 0;

	/// <summary>Factory used by the generated backend registration.</summary>
	public static WslContainerBackend Create() => new();

	/// <summary>
	/// Creates a backend from the runtime options forwarded by the portable facade. The facade cannot pass
	/// <see cref="WslContainerRuntimeOptions" /> across the payload boundary directly (each build has its own
	/// copy of the type), so it forwards the individual primitive values instead.
	/// </summary>
	internal static WslContainerBackend Create(
		uint? cpuCount,
		uint? memorySizeInMB,
		bool enableGpu,
		string? sessionName,
		string? storagePath,
		int storageMode,
		long? sessionTimeoutTicks,
		bool disableProcessExitCleanup
	)
	{
		WslContainerRuntimeOptions options = new()
		{
			CPUCount = cpuCount,
			MemorySizeInMB = memorySizeInMB,
			EnableGPU = enableGpu,
			SessionName = sessionName,
			StoragePath = storagePath,
			StorageMode = (StorageMode)storageMode,
			SessionTimeout = sessionTimeoutTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
			DisableProcessExitCleanup = disableProcessExitCleanup,
		};
		return new WslContainerBackend(options);
	}

	IContainerRuntime Runtime => field ?? WslContainerRuntime.Instance;

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
