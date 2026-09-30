using Purview.WslContainers.Diagnostics;
using Purview.WslContainers.Images;
using Purview.WslContainers.Mounts;
using Purview.WslContainers.Networking;
using Purview.WslContainers.Waiting;
using System.Text;

namespace Purview.WslContainers;

/// <summary>Immutable configuration for a generic WSLC container.</summary>
public record ContainerConfiguration : IContainerConfiguration
{
	/// <inheritdoc />
	public string Image { get; init; } = string.Empty;

	/// <inheritdoc />
	public string? Name { get; init; }

	/// <inheritdoc />
	public string? Hostname { get; init; }

	/// <inheritdoc />
	public string? DomainName { get; init; }

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string> Environment { get; init; } =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <inheritdoc />
	public IReadOnlyList<string> Command { get; init; } = Array.Empty<string>();

	/// <inheritdoc />
	public string? WorkingDirectory { get; init; }

	/// <inheritdoc />
	public IReadOnlyList<PortBinding> PortBindings { get; init; } = Array.Empty<PortBinding>();

	/// <inheritdoc />
	public IReadOnlyList<BindMount> BindMounts { get; init; } = Array.Empty<BindMount>();

	/// <inheritdoc />
	public IReadOnlyList<NamedVolume> NamedVolumes { get; init; } = Array.Empty<NamedVolume>();

	/// <inheritdoc />
	public ContainerNetworkingMode NetworkingMode { get; init; } = ContainerNetworkingMode.Bridged;

	/// <inheritdoc />
	public bool Privileged { get; init; }

	/// <inheritdoc />
	public bool EnableGPU { get; init; }

	/// <inheritdoc />
	public bool EnableAutoRemove { get; init; }

	/// <inheritdoc />
	public PullPolicy PullPolicy { get; init; } = PullPolicy.Missing;

	/// <inheritdoc />
	public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(5);

	/// <inheritdoc />
	public IReadOnlyList<IWaitStrategy> WaitStrategies { get; init; } = Array.Empty<IWaitStrategy>();

	/// <inheritdoc />
	public RegistryCredentials? RegistryCredentials { get; init; }

	/// <summary>
	/// Renders the configuration, redacting environment variables whose keys look sensitive
	/// (PASSWORD, TOKEN, SECRET, KEY, ...). Secrets wrapped in <see cref="Secret" /> always render as redacted.
	/// </summary>
	protected virtual bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Append(nameof(Image)).Append(" = ").Append(Image);
		builder.Append(", ").Append(nameof(Name)).Append(" = ").Append(Name);
		builder.Append(", ").Append(nameof(Hostname)).Append(" = ").Append(Hostname);
		builder.Append(", ").Append(nameof(DomainName)).Append(" = ").Append(DomainName);
		builder
			.Append(", ")
			.Append(nameof(Environment))
			.Append(" = ")
			.Append(SecretRedactor.RedactEnvironment(Environment));
		builder.Append(", ").Append(nameof(Command)).Append(" = ").Append(Command);
		builder.Append(", ").Append(nameof(WorkingDirectory)).Append(" = ").Append(WorkingDirectory);
		builder.Append(", ").Append(nameof(PortBindings)).Append(" = ").Append(PortBindings);
		builder.Append(", ").Append(nameof(BindMounts)).Append(" = ").Append(BindMounts);
		builder.Append(", ").Append(nameof(NamedVolumes)).Append(" = ").Append(NamedVolumes);
		builder.Append(", ").Append(nameof(NetworkingMode)).Append(" = ").Append(NetworkingMode);
		builder.Append(", ").Append(nameof(Privileged)).Append(" = ").Append(Privileged);
		builder.Append(", ").Append(nameof(EnableGPU)).Append(" = ").Append(EnableGPU);
		builder.Append(", ").Append(nameof(EnableAutoRemove)).Append(" = ").Append(EnableAutoRemove);
		builder.Append(", ").Append(nameof(PullPolicy)).Append(" = ").Append(PullPolicy);
		builder.Append(", ").Append(nameof(StartupTimeout)).Append(" = ").Append(StartupTimeout);
		builder.Append(", ").Append(nameof(WaitStrategies)).Append(" = ").Append(WaitStrategies);

		return true;
	}
}
