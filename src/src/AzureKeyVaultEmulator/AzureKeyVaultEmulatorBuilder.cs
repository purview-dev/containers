using System.Runtime.InteropServices;
using Purview.Containers.Mounts;
using Purview.Containers.Networking;
using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.AzureKeyVaultEmulator;

/// <summary>
/// Fluent builder for an <see href="https://github.com/james-gould/azure-keyvault-emulator">Azure Key Vault
/// Emulator</see> test container. The emulator serves the full Key Vault REST API (secrets, keys,
/// certificates) over HTTPS on a single port, so the Azure SDK clients work against it unchanged.
/// </summary>
public class AzureKeyVaultEmulatorBuilder
	: ContainerBuilder<AzureKeyVaultEmulatorBuilder, AzureKeyVaultEmulatorContainer, AzureKeyVaultEmulatorConfiguration>
{
	/// <summary>Default emulator port (HTTPS only).</summary>
	public const ushort EmulatorPort = 4997;

	/// <summary>Default image repository.</summary>
	public const string EmulatorImage = "jamesgoulddev/azure-keyvault-emulator";

	/// <summary>Default image tag (x64). The newest tag published to Docker Hub; <c>latest</c> tracks it.</summary>
	public const string EmulatorTag = "3.1.3";

	/// <summary>Default image tag (ARM64).</summary>
	public const string EmulatorArmTag = "3.1.3-arm";

	/// <summary>Container path the certificate directory is mounted at.</summary>
	public const string CertificateMountPath = "/certs";

	/// <summary>Password of the emulator PFX. Fixed by the image: Kestrel is configured with it up front.</summary>
	public const string CertificatePassword = "emulator";

	bool _persist;
	bool _generateCertificates = true;
	bool _installCertificatesIntoTrustStore = true;
	bool _cleanupCertificatesOnDispose;
	ushort? _fixedHostPort;
	string? _certificateDirectory;

	/// <summary>Creates a builder with the default image.</summary>
	public AzureKeyVaultEmulatorBuilder()
		: this(DefaultImageReference) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public AzureKeyVaultEmulatorBuilder(string image)
	{
		ApplyDefaults(image);
	}

	/// <summary>Creates a builder using an explicit backend.</summary>
	public AzureKeyVaultEmulatorBuilder(IContainerBackend backend)
		: base(backend)
	{
		ApplyDefaults(DefaultImageReference);
	}

	/// <summary>The default image reference for this host's process architecture.</summary>
	public static string DefaultImageReference =>
		RuntimeInformation.ProcessArchitecture == Architecture.Arm64
			? $"{EmulatorImage}:{EmulatorArmTag}"
			: $"{EmulatorImage}:{EmulatorTag}";

	/// <summary>
	/// Persists vault data in an <c>emulator.db</c> next to the certificates. Requires
	/// <see cref="WithFixedHostPort" />: the persisted data embeds the vault URI, so a random port would
	/// leave it unreachable after a restart.
	/// </summary>
	public AzureKeyVaultEmulatorBuilder WithPersistence(bool persist = true)
	{
		_persist = persist;
		return this;
	}

	/// <summary>
	/// Uses <paramref name="path" /> as the host certificate directory mounted at
	/// <see cref="CertificateMountPath" />. It must contain <c>emulator.pfx</c> (password
	/// <see cref="CertificatePassword" />) and <c>emulator.crt</c>; pair the two methods with
	/// <see cref="WithGeneratedCertificates" /> when the files must exist rather than be generated.
	/// </summary>
	public AzureKeyVaultEmulatorBuilder WithCertificateDirectory(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		_certificateDirectory = path;
		return this;
	}

	/// <summary>
	/// Generates a self-signed certificate when the certificate directory has none (default). Disable it to
	/// require an explicit <see cref="WithCertificateDirectory" />; <c>Build()</c> throws otherwise.
	/// </summary>
	public AzureKeyVaultEmulatorBuilder WithGeneratedCertificates(bool generate = true)
	{
		_generateCertificates = generate;
		return this;
	}

	/// <summary>
	/// Installs the certificates into the host trust store (default). Disable it when the test host must
	/// not be modified: the module's own clients pin the certificate, so they never need the trust store.
	/// </summary>
	public AzureKeyVaultEmulatorBuilder WithTrustStoreInstallation(bool install = true)
	{
		_installCertificatesIntoTrustStore = install;
		return this;
	}

	/// <summary>Uninstalls and deletes the generated certificates on dispose. Default is to keep them.</summary>
	public AzureKeyVaultEmulatorBuilder WithCertificateCleanup(bool cleanup = true)
	{
		_cleanupCertificatesOnDispose = cleanup;
		return this;
	}

	/// <summary>
	/// Pins the host port the emulator is exposed on. Required with <see cref="WithPersistence" />,
	/// because the persisted data embeds the vault URI.
	/// </summary>
	public AzureKeyVaultEmulatorBuilder WithFixedHostPort(ushort hostPort)
	{
		if (hostPort == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(hostPort),
				hostPort,
				"Host port 0 is invalid; the emulator needs a real port."
			);
		}

		_fixedHostPort = hostPort;
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal AzureKeyVaultEmulatorConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override AzureKeyVaultEmulatorConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();

		var certificateDirectory = _certificateDirectory ?? AzureKeyVaultEmulatorCertificates.DefaultDirectory;
		List<BindMount> bindMounts =
		[
			.. configuration.BindMounts,
			new BindMount(certificateDirectory, CertificateMountPath, ReadOnly: true),
		];

		Dictionary<string, string> environment = new(configuration.Environment, StringComparer.Ordinal)
		{
			// Always sent so the image default can never surprise a test run.
			["Persist"] = _persist ? "true" : "false",
		};

		var waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)BuildDefaultWaitStrategy() };

		return configuration with
		{
			Environment = environment,
			BindMounts = bindMounts,
			PortBindings = ResolvePortBindings(configuration.PortBindings),
			WaitStrategies = waitStrategies,
			Persist = _persist,
			CertificateDirectory = certificateDirectory,
			GenerateCertificates = _generateCertificates,
			InstallCertificatesIntoTrustStore = _installCertificatesIntoTrustStore,
			CleanupCertificatesOnDispose = _cleanupCertificatesOnDispose,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);

		if (configuration is not AzureKeyVaultEmulatorConfiguration emulatorConfiguration)
		{
			return;
		}

		if (
			emulatorConfiguration.Persist
			&& !emulatorConfiguration.PortBindings.Any(binding =>
				binding.ContainerPort == EmulatorPort && !binding.AssignRandomHostPort
			)
		)
		{
			throw new ContainerConfigurationException(
				"Persistence requires a fixed host port: the emulator embeds the vault URI in its persisted data, so a random port would leave that data unreachable after a restart. Call WithFixedHostPort(...) before Build()."
			);
		}

		if (!_generateCertificates && string.IsNullOrWhiteSpace(_certificateDirectory))
		{
			throw new ContainerConfigurationException(
				"Certificate generation is disabled but no certificate directory was supplied. Call WithCertificateDirectory(...) with a directory containing emulator.pfx (and emulator.crt)."
			);
		}
	}

	/// <inheritdoc />
	protected override AzureKeyVaultEmulatorContainer CreateContainer(
		AzureKeyVaultEmulatorConfiguration configuration
	) => new(configuration, Backend);

	WaitStrategy BuildDefaultWaitStrategy()
	{
		// GET / is served unauthenticated once Kestrel is listening; any HTTP response proves the HTTPS
		// stack is up. AllowInsecureTls keeps the probe independent of the host trust store.
		return Wait.ForHttp("/")
			.ForPort(EmulatorPort)
			.ForScheme("https")
			.AllowInsecureTls()
			.ForStatusPredicate(static _ => true)
			.WithInterval(TimeSpan.FromSeconds(1));
	}

	IReadOnlyList<PortBinding> ResolvePortBindings(IReadOnlyList<PortBinding> bindings)
	{
		if (_fixedHostPort is not ushort hostPort)
		{
			return bindings;
		}

		// Replace the module's default random binding rather than adding a second one.
		return
		[
			.. bindings.Select(binding =>
				binding.ContainerPort == EmulatorPort && binding.AssignRandomHostPort
					? binding with
					{
						HostPort = hostPort,
					}
					: binding
			),
		];
	}

	void ApplyDefaults(string image)
	{
		WithImage(image)
			.WithPortBinding(EmulatorPort, assignRandomHostPort: true)
			.WithConnectionStringProvider(new AzureKeyVaultEmulatorConnectionStringProvider());
	}
}
