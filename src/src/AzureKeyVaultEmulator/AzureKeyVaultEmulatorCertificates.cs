using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Purview.Containers.Runtime;

namespace Purview.Containers.AzureKeyVaultEmulator;

/// <summary>Generates, loads, installs and removes the emulator's self-signed certificate pair.</summary>
static class AzureKeyVaultEmulatorCertificates
{
	internal const string PfxFileName = "emulator.pfx";
	internal const string CrtFileName = "emulator.crt";

	const string Subject = "CN=localhost";
	const string HostParentDirectory = "keyvaultemulator";
	const string HostChildDirectory = "certs";
	const string LinuxCaCertificateDirectory = "/usr/local/share/ca-certificates";
	const string ServerAuthenticationEnhancedKeyUsage = "1.3.6.1.5.5.7.3.1";

	// The same variables the emulator's own TestContainers module detects, so the throwaway certificates
	// stay out of a runner's user profile.
	static readonly string[] ContinuousIntegrationVariables = ["BUILD_BUILDID", "CI", "GITHUB_ACTIONS", "TF_BUILD"];

	internal static string DefaultDirectory { get; } = ResolveDefaultDirectory();

	/// <summary>
	/// Returns the certificate the emulator must be started with, generating it (when allowed) into the
	/// configured directory. The generated flag reports whether this call created the pair.
	/// </summary>
	internal static (X509Certificate2 Certificate, bool Generated) EnsureCertificate(
		AzureKeyVaultEmulatorConfiguration configuration
	)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentException.ThrowIfNullOrWhiteSpace(configuration.CertificateDirectory);

		Directory.CreateDirectory(configuration.CertificateDirectory);

		var pfxPath = Path.Combine(configuration.CertificateDirectory, PfxFileName);
		var crtPath = Path.Combine(configuration.CertificateDirectory, CrtFileName);

		if (File.Exists(pfxPath) && File.Exists(crtPath))
		{
			return (LoadPfx(pfxPath), Generated: false);
		}

		if (!configuration.GenerateCertificates)
		{
			throw new ContainerConfigurationException(
				$"No certificate pair was found in '{configuration.CertificateDirectory}' and certificate generation is disabled. Provide '{PfxFileName}' (password '{AzureKeyVaultEmulatorBuilder.CertificatePassword}') and '{CrtFileName}', or re-enable generated certificates."
			);
		}

		// Half of the pair has gone missing: remove both so the regenerated pair is consistent.
		TryDelete(pfxPath);
		TryDelete(crtPath);

		return (GenerateAndExport(pfxPath, crtPath), Generated: true);
	}

	/// <summary>Installs the certificate into the host trust store so unmodified Azure SDK clients connect.</summary>
	internal static void InstallIntoTrustStore(X509Certificate2 certificate, string certificateDirectory)
	{
		ArgumentNullException.ThrowIfNull(certificate);

		if (OperatingSystem.IsWindows())
		{
			InstallIntoWindowsTrustStore(certificate);
			return;
		}

		if (OperatingSystem.IsLinux())
		{
			InstallIntoLinuxTrustStore(certificate);
			return;
		}

		if (OperatingSystem.IsMacOS())
		{
			// .NET cannot add a trust anchor to the macOS system keychain; tell the caller what to run.
			Console.WriteLine("To install the emulator certificate into the macOS trust store, run:");
			Console.WriteLine(
				$"sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain \"{Path.Combine(certificateDirectory, CrtFileName)}\""
			);
		}
	}

	/// <summary>Removes the certificate from the trust store (Windows only; other platforms are untouched).</summary>
	internal static void UninstallFromTrustStore(X509Certificate2 certificate)
	{
		ArgumentNullException.ThrowIfNull(certificate);

		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		using X509Store store = new(StoreName.Root, StoreLocation.CurrentUser);
		store.Open(OpenFlags.ReadWrite);

		var matches = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);
		foreach (var match in matches)
		{
			store.Remove(match);
			match.Dispose();
		}

		store.Close();
	}

	/// <summary>Deletes a generated certificate pair. Caller-supplied directories are never touched.</summary>
	internal static void DeleteCertificates(string certificateDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(certificateDirectory);

		TryDelete(Path.Combine(certificateDirectory, PfxFileName));
		TryDelete(Path.Combine(certificateDirectory, CrtFileName));
	}

	static void TryDelete(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	static string ResolveDefaultDirectory()
	{
		var path = Path.Combine(HostParentDirectory, HostChildDirectory);

		// CI runners are ephemeral: keep the throwaway certificates out of the user profile.
		if (ContinuousIntegrationVariables.Any(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))))
		{
			return Path.Combine(Path.GetTempPath(), path);
		}

		var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		return string.IsNullOrWhiteSpace(profile)
			? Path.Combine(Path.GetTempPath(), path)
			: Path.Combine(profile, path);
	}

	static X509Certificate2 LoadPfx(string pfxPath)
	{
		try
		{
			return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, AzureKeyVaultEmulatorBuilder.CertificatePassword);
		}
		catch (CryptographicException exception)
		{
			throw new ContainerConfigurationException(
				$"The certificate at '{pfxPath}' could not be loaded. The PFX password must be '{AzureKeyVaultEmulatorBuilder.CertificatePassword}'.",
				exception
			);
		}
	}

	static X509Certificate2 GenerateAndExport(string pfxPath, string crtPath)
	{
		X500DistinguishedName subject = new(Subject);
		using var key = RSA.Create();

		CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
		request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
		request.CertificateExtensions.Add(BuildSubjectAlternativeNames());
		request.CertificateExtensions.Add(
			new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
				critical: true
			)
		);
		request.CertificateExtensions.Add(
			new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationEnhancedKeyUsage)], critical: false)
		);

		var certificate = request.CreateSelfSigned(
			DateTimeOffset.UtcNow.AddDays(-1),
			DateTimeOffset.UtcNow.AddYears(1)
		);

		// FriendlyName assignment is only supported on Windows.
		if (OperatingSystem.IsWindows())
		{
			certificate.FriendlyName = "Azure Key Vault Emulator";
		}

		File.WriteAllBytes(
			pfxPath,
			certificate.Export(X509ContentType.Pfx, AzureKeyVaultEmulatorBuilder.CertificatePassword)
		);
		File.WriteAllText(crtPath, ExportToPem(certificate));

		return certificate;
	}

	static X509Extension BuildSubjectAlternativeNames()
	{
		SubjectAlternativeNameBuilder builder = new();

		builder.AddDnsName("host.docker.internal");
		builder.AddDnsName("localhost");
		builder.AddIpAddress(IPAddress.Parse("127.0.0.1"));

		return builder.Build();
	}

	static string ExportToPem(X509Certificate2 certificate) => certificate.ExportCertificatePem();

	static void InstallIntoWindowsTrustStore(X509Certificate2 certificate)
	{
		using X509Store store = new(StoreName.Root, StoreLocation.CurrentUser);
		store.Open(OpenFlags.ReadWrite);

		// Adding a root anchor is the point of this call: the emulator serves a self-signed certificate and
		// the Azure SDK enforces HTTPS. The certificate is the module's own throwaway one, never a CA.
#pragma warning disable CA5380 // Do Not Add Certificates To Root Certificate Store In Windows
		store.Add(certificate);
#pragma warning restore CA5380 // Do Not Add Certificates To Root Certificate Store In Windows

		store.Close();
	}

	static void InstallIntoLinuxTrustStore(X509Certificate2 certificate)
	{
		var destination = Path.Combine(LinuxCaCertificateDirectory, CrtFileName);
		var stagedPath = Path.Combine(Path.GetTempPath(), CrtFileName);

		// /usr/local/share/ca-certificates is root-owned, so stage the PEM somewhere writable and copy it
		// with sudo; rebuilding the CA bundle needs sudo too.
		File.WriteAllText(stagedPath, ExportToPem(certificate));
		RunBash($"sudo cp '{stagedPath}' '{destination}'");
		RunBash("sudo update-ca-certificates");
	}

	static void RunBash(string command)
	{
		ProcessStartInfo startInfo = new()
		{
			FileName = "/bin/bash",
			ArgumentList = { "-c", command },
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		using var process =
			Process.Start(startInfo)
			?? throw new ContainerConfigurationException($"Failed to start /bin/bash for: {command}");

		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();

		if (process.ExitCode != 0)
		{
			throw new ContainerConfigurationException(
				$"Installing the emulator certificate into the Linux trust store failed (exit code {process.ExitCode}): {error.Trim()}. "
					+ "sudo is required; alternatively disable trust-store installation with WithTrustStoreInstallation(false) — the module's own clients pin the certificate and do not need it."
			);
		}
	}
}
