using Purview.WslContainers;
using Purview.WslContainers.Images;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.UnitTests;

public class ContainerBuilderTests
{
	private sealed class TestBuilder : ContainerBuilder<TestBuilder, WslContainer, ContainerConfiguration>
	{
		public ContainerConfiguration BuildConfig() => BuildConfiguration();

		public void ValidateConfig(ContainerConfiguration configuration) => Validate(configuration);

		protected override WslContainer CreateContainer(ContainerConfiguration configuration)
		{
			return new WslContainer(configuration, WslContainerRuntime.Instance);
		}
	}

	[Test]
	public async Task BuildConfiguration_PopulatesAllCommonFields()
	{
		ContainerConfiguration configuration = new TestBuilder()
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
		await Assert.That(configuration.Command).IsEquivalentTo(new[] { "sleep", "10" });
		await Assert.That(configuration.WorkingDirectory).IsEqualTo("/tmp");
		await Assert.That(configuration.PortBindings).HasCount().EqualTo(2);
		await Assert.That(configuration.PortBindings[0].AssignRandomHostPort).IsTrue();
		await Assert.That(configuration.PortBindings[1].HostPort).IsEqualTo((ushort)9091);
		await Assert.That(configuration.BindMounts).HasCount().EqualTo(1);
		await Assert.That(configuration.NamedVolumes).HasCount().EqualTo(1);
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
		TestBuilder builder = new TestBuilder().WithImage("alpine").WithPortBinding(53, 53, PortProtocol.Udp);
		await Assert.That(() => builder.Build()).Throws<WslContainerNotSupportedException>();
	}

	[Test]
	public async Task Build_WithContainerPortZero_Throws()
	{
		TestBuilder builder = new TestBuilder().WithImage("alpine").WithPortBinding(0, assignRandomHostPort: true);
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithHostPortZero_Throws()
	{
		TestBuilder builder = new TestBuilder().WithImage("alpine").WithPortBinding(80, 0);
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithEmptyBindMount_Throws()
	{
		TestBuilder builder = new TestBuilder().WithImage("alpine").WithBindMount(string.Empty, "/data");
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithZeroStartupTimeout_Throws()
	{
		TestBuilder builder = new TestBuilder().WithImage("alpine").WithStartupTimeout(TimeSpan.Zero);
		await Assert.That(() => builder.Build()).Throws<WslContainerConfigurationException>();
	}

	[Test]
	public async Task GenericContainerBuilder_GeneratesUniqueName()
	{
		WslContainer first = new ContainerBuilder().WithImage("alpine").Build();
		WslContainer second = new ContainerBuilder().WithImage("alpine").Build();

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
		ContainerConfiguration configuration = new TestBuilder().WithImage("redis").WithTag("7.4").BuildConfig();

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
