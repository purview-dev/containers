using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;

namespace Purview.Containers.AzureKeyVaultEmulator;

/// <summary>
/// A throwaway <see href="https://github.com/james-gould/azure-keyvault-emulator">Azure Key Vault
/// Emulator</see> instance running on WSL Containers or Docker. The Azure SDK clients work against it
/// unchanged; the module hands out ones that are already wired for the emulator.
/// </summary>
public sealed class AzureKeyVaultEmulatorContainer : ContainerBase
{
	readonly AzureKeyVaultEmulatorConfiguration _configuration;

	int _prepared;
	X509Certificate2? _certificate;
	bool _generatedCertificates;
	bool _installedCertificates;
	HttpClient? _httpClient;
	TokenCredential? _credential;

	internal AzureKeyVaultEmulatorContainer(
		AzureKeyVaultEmulatorConfiguration configuration,
		IContainerBackend? backend
	)
		: base(configuration, backend)
	{
		_configuration = configuration;
	}

	/// <summary>Vault URI (<c>https://127.0.0.1:{mappedPort}</c>). Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public Uri GetVaultUri()
	{
		// 127.0.0.1 is required: WSLC maps IPv4 loopback only, and 'localhost' resolves to IPv6 ::1.
		return new Uri(
			$"https://127.0.0.1:{GetMappedPublicPort(AzureKeyVaultEmulatorBuilder.EmulatorPort)}",
			UriKind.Absolute
		);
	}

	/// <summary>The vault URI. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public string GetConnectionString() => GetVaultUri().ToString();

	/// <summary>The certificate the emulator serves. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public X509Certificate2 GetCertificate() =>
		_certificate ?? throw new InvalidOperationException("Container has not been started. Call StartAsync() first.");

	/// <summary>A SecretClient configured for the emulator. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public SecretClient GetSecretClient()
	{
		SecretClientOptions options = new()
		{
			DisableChallengeResourceVerification = true,
			Transport = CreateTransport(),
		};
		return new SecretClient(GetVaultUri(), CreateCredential(), options);
	}

	/// <summary>A KeyClient configured for the emulator. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public KeyClient GetKeyClient()
	{
		KeyClientOptions options = new() { DisableChallengeResourceVerification = true, Transport = CreateTransport() };
		return new KeyClient(GetVaultUri(), CreateCredential(), options);
	}

	/// <summary>A CertificateClient configured for the emulator. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public CertificateClient GetCertificateClient()
	{
		CertificateClientOptions options = new()
		{
			DisableChallengeResourceVerification = true,
			Transport = CreateTransport(),
		};
		return new CertificateClient(GetVaultUri(), CreateCredential(), options);
	}

	/// <inheritdoc />
	public override async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (Interlocked.Exchange(ref _prepared, 1) == 1)
		{
			return;
		}

		// Ensure the certificate pair exists (and install it when asked) before the backend creates the
		// container, so the certificate bind mount has files to expose.
		(_certificate, _generatedCertificates) = AzureKeyVaultEmulatorCertificates.EnsureCertificate(_configuration);

		if (_configuration.InstallCertificatesIntoTrustStore)
		{
			AzureKeyVaultEmulatorCertificates.InstallIntoTrustStore(_certificate, _configuration.CertificateDirectory);
			_installedCertificates = true;
		}

		await base.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async ValueTask DisposeAsync()
	{
		if (_configuration.CleanupCertificatesOnDispose)
		{
			if (_installedCertificates && _certificate is not null)
			{
				AzureKeyVaultEmulatorCertificates.UninstallFromTrustStore(_certificate);
			}

			if (_generatedCertificates)
			{
				AzureKeyVaultEmulatorCertificates.DeleteCertificates(_configuration.CertificateDirectory);
			}
		}

		_credential = null;
		_httpClient?.Dispose();
		_httpClient = null;

		await base.DisposeAsync().ConfigureAwait(false);
	}

	HttpClient PinningHttpClient
	{
		get
		{
			if (_httpClient is not null)
			{
				return _httpClient;
			}

#pragma warning disable CA2000 // Dispose objects before losing scope
			// HttpClient owns the handler: it disposes it when the client is disposed (in DisposeAsync).
			_httpClient = new HttpClient(CreatePinningHandler()) { Timeout = TimeSpan.FromSeconds(30) };
#pragma warning restore CA2000 // Dispose objects before losing scope
			return _httpClient;
		}
	}

	TokenCredential CreateCredential() =>
		_credential ??= new AzureKeyVaultEmulatorTokenCredential(PinningHttpClient, GetVaultUri());

	HttpClientTransport CreateTransport() => new(PinningHttpClient);

	// HttpClient takes ownership of the handler and disposes it with itself, so the handler is not leaked.
	HttpClientHandler CreatePinningHandler() =>
		new()
		{
			// The emulator serves a self-signed certificate, so the module's clients pin it. That keeps
			// them working without the certificate being present in the host trust store, which is what
			// makes the module portable across Windows, Linux and CI runners.
			ServerCertificateCustomValidationCallback = ValidateServerCertificate,
		};

	bool ValidateServerCertificate(
		HttpRequestMessage request,
		X509Certificate2? certificate,
		X509Chain? chain,
		SslPolicyErrors errors
	)
	{
		if (certificate is null)
		{
			return false;
		}

		var emulatorCertificate = _certificate;
		if (emulatorCertificate is null)
		{
			return errors == SslPolicyErrors.None;
		}

		// The emulator serves a self-signed certificate, so the module's clients pin it. That keeps them working without the certificate being present in the host trust store, which is what makes the module portable across Windows, Linux and CI runners.
		return string.Equals(
			certificate.Thumbprint,
			emulatorCertificate.Thumbprint,
			StringComparison.OrdinalIgnoreCase
		);
	}
}
