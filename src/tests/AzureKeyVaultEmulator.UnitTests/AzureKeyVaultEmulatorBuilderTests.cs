using System.Runtime.InteropServices;
using Purview.Containers.Runtime;
using Purview.Containers.Waiting;

namespace Purview.Containers.AzureKeyVaultEmulator;

public class AzureKeyVaultEmulatorBuilderTests
{
	[Test]
	public async Task BuildConfig_AppliesDefaults()
	{
		var configuration = new AzureKeyVaultEmulatorBuilder().BuildConfigurationForTesting();

		await Assert
			.That(configuration.Image)
			.IsEqualTo($"docker.io/{AzureKeyVaultEmulatorBuilder.EmulatorImage}:{ExpectedTag()}");
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(1);
		await Assert.That(configuration.PortBindings[0].ContainerPort).IsEqualTo((ushort)4997);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsTrue();
		await Assert.That(configuration.BindMounts.Count).IsEqualTo(1);
		await Assert.That(configuration.BindMounts[0].ContainerPath).IsEqualTo("/certs");
		await Assert.That(configuration.BindMounts[0].ReadOnly).IsTrue();
		await Assert.That(configuration.Environment["Persist"]).IsEqualTo("false");
		await Assert
			.That(configuration.CertificateDirectory)
			.IsEqualTo(AzureKeyVaultEmulatorCertificates.DefaultDirectory);
		await Assert.That(configuration.GenerateCertificates).IsTrue();
		await Assert.That(configuration.InstallCertificatesIntoTrustStore).IsTrue();
		await Assert.That(configuration.CleanupCertificatesOnDispose).IsFalse();
		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<HttpWaitStrategy>();
	}

	[Test]
	public async Task BuildConfig_PersistenceWithAFixedHostPort_PinsTheBinding()
	{
		var configuration = new AzureKeyVaultEmulatorBuilder()
			.WithPersistence()
			.WithFixedHostPort(4997)
			.BuildConfigurationForTesting();

		await Assert.That(configuration.Persist).IsTrue();
		await Assert.That(configuration.Environment["Persist"]).IsEqualTo("true");
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(1);
		await Assert.That(configuration.PortBindings[0].HostPort).IsEqualTo((ushort)4997);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsFalse();
	}

	[Test]
	public async Task Build_PersistenceWithoutAFixedHostPort_Throws()
	{
		var builder = new AzureKeyVaultEmulatorBuilder().WithPersistence();

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task BuildConfig_CertificateDirectory_IsMountedReadOnlyAtCerts()
	{
		var configuration = new AzureKeyVaultEmulatorBuilder()
			.WithCertificateDirectory("certs/emulator")
			.BuildConfigurationForTesting();

		await Assert.That(configuration.CertificateDirectory).IsEqualTo("certs/emulator");
		await Assert.That(configuration.BindMounts.Count).IsEqualTo(1);
		await Assert.That(configuration.BindMounts[0].HostPath).IsEqualTo("certs/emulator");
		await Assert.That(configuration.BindMounts[0].ContainerPath).IsEqualTo("/certs");
		await Assert.That(configuration.BindMounts[0].ReadOnly).IsTrue();
	}

	[Test]
	public async Task Build_WithoutGeneratedCertificatesAndNoDirectory_Throws()
	{
		var builder = new AzureKeyVaultEmulatorBuilder().WithGeneratedCertificates(false);

		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task BuildConfig_TrustStoreAndCleanupFlags_AreHonoured()
	{
		var configuration = new AzureKeyVaultEmulatorBuilder()
			.WithTrustStoreInstallation(false)
			.WithCertificateCleanup()
			.BuildConfigurationForTesting();

		await Assert.That(configuration.InstallCertificatesIntoTrustStore).IsFalse();
		await Assert.That(configuration.CleanupCertificatesOnDispose).IsTrue();
	}

	[Test]
	public async Task BuildConfig_PreservesCustomWaitStrategy()
	{
		var configuration = new AzureKeyVaultEmulatorBuilder()
			.WithWaitStrategy(Wait.ForTcpPort(4997))
			.BuildConfigurationForTesting();

		await Assert.That(configuration.WaitStrategies.Count).IsEqualTo(1);
		await Assert.That(configuration.WaitStrategies[0]).IsTypeOf<TcpPortWaitStrategy>();
	}

	static string ExpectedTag() =>
		RuntimeInformation.ProcessArchitecture == Architecture.Arm64
			? AzureKeyVaultEmulatorBuilder.EmulatorArmTag
			: AzureKeyVaultEmulatorBuilder.EmulatorTag;
}
