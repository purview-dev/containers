using Purview.WslContainers.Images;
using Purview.WslContainers.Networking;
using Purview.WslContainers.Runtime;

namespace Purview.WslContainers;

class ContainerBuilderTests
{
	sealed class TestBuilder : ContainerBuilder<TestBuilder, WslContainer, ContainerConfiguration>
	{
		public ContainerConfiguration BuildConfig() => BuildConfiguration();

		public void ValidateConfig(ContainerConfiguration configuration) => Validate(configuration);

		protected override WslContainer CreateContainer(ContainerConfiguration configuration)
		{
			return new WslContainer(configuration, WslContainerRuntime.Instance);
		}
	}

	static readonly string[] Expected = ["sleep", "10"];

	[Test]
	public async Task BuildConfiguration_PopulatesAllCommonFields()
	{
		var configuration = new TestBuilder()
			.WithImage("alpine:3.19")
			.WithName("unit-test")
			.WithHostname("unit")
			.WithEnvironment("A", "1")
			.WithEnvironment("B", "2")
			.WithCommand("sleep", "10")
			.WithWorkingDirectory("/tmp")
			.WithPortBinding(8080, assignRandomHostPort: true)
			.WithPortBinding(9090, hostPort: 9091)
			.WithBindMount(@"C:\tmp", "/data")
			.WithVolumeMount("vol", "/vol")
			.BuildConfig();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/alpine:3.19");
		await Assert.That(configuration.Name).IsEqualTo("unit-test");
		await Assert.That(configuration.Hostname).IsEqualTo("unit");
		await Assert.That(configuration.Environment["A"]).IsEqualTo("1");
		await Assert.That(configuration.Environment["B"]).IsEqualTo("2");
		await Assert.That(configuration.Command).IsEquivalentTo(Expected);
		await Assert.That(configuration.WorkingDirectory).IsEqualTo("/tmp");
		await Assert.That(configuration.PortBindings.Count).IsEqualTo(2);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsTrue();
		await Assert.That(configuration.PortBindings[1].HostPort).IsEqualTo((ushort)9091);
		await Assert.That(configuration.BindMounts.Count).IsEqualTo(1);
		await Assert.That(configuration.NamedVolumes.Count).IsEqualTo(1);
		await Assert.That(configuration.NetworkingMode).IsEqualTo(ContainerNetworkingMode.Bridged);
		await Assert.That(configuration.PullPolicy).IsEqualTo(PullPolicy.Missing);
	}

	[Test]
	public async Task Build_WithoutImage_Throws()
	{
		await Assert.That(() => new TestBuilder().Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithUdpPort_ThrowsNotSupported()
	{
		var builder = new TestBuilder().WithImage("alpine").WithPortBinding(53, 53, PortProtocol.Udp);
		await Assert.That(() => builder.Build()).Throws<WslContainerNotSupportedException>();
	}

	[Test]
	public async Task Build_WithContainerPortZero_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithPortBinding(0, assignRandomHostPort: true);
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithHostPortZero_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithPortBinding(80, 0);
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithEmptyBindMount_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithBindMount(string.Empty, "/data");
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithZeroStartupTimeout_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithStartupTimeout(TimeSpan.Zero);
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task GenericContainerBuilder_GeneratesUniqueName()
	{
		var first = new ContainerBuilder().WithImage("alpine").Build();
		var second = new ContainerBuilder().WithImage("alpine").Build();

		await Assert.That(first.Name).IsNotEqualTo(second.Name);
		await Assert.That(first.Image).IsEqualTo("docker.io/library/alpine:latest");
	}

	[Test]
	public async Task WithImage_InvalidReference_ThrowsImmediately()
	{
		await Assert
			.That(() => new ContainerBuilder().WithImage("bad image name"))
			.Throws<WslContainerConfigurationException>();
		await Assert
			.That(() => new ContainerBuilder().WithImage(string.Empty))
			.Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task WithTag_AppliesTagToConfiguredImage()
	{
		var configuration = new TestBuilder().WithImage("redis").WithTag("7.4").BuildConfig();

		await Assert.That(configuration.Image).IsEqualTo("docker.io/library/redis:7.4");
	}

	[Test]
	public async Task WithTag_WithoutImage_Throws()
	{
		await Assert.That(() => new ContainerBuilder().WithTag("7.4")).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task WithTag_InvalidTag_Throws()
	{
		await Assert
			.That(() => new ContainerBuilder().WithImage("redis").WithTag("bad tag"))
			.Throws<WslContainerConfigurationException>();
	}
}
