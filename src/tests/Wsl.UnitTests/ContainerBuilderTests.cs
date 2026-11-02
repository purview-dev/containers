using Purview.Containers.Images;
using Purview.Containers.Networking;
using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

public class ContainerBuilderTests
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
	public async Task BuildConfiguration_BuilderChanges_DoNotMutateBuiltConfiguration()
	{
		// Arrange
		var builder = new TestBuilder()
			.WithImage("alpine")
			.WithCommand("sleep", "1")
			.WithEnvironment("VALUE", "original")
			.WithLabel("label", "original");
		var configuration = builder.BuildConfig();

		// Act
		builder.WithCommand("false").WithEnvironment("VALUE", "changed").WithLabel("label", "changed");

		// Assert
		await Assert.That(configuration.Command[0]).IsEqualTo("sleep");
		await Assert.That(configuration.Environment["VALUE"]).IsEqualTo("original");
		await Assert.That(configuration.Labels["label"]).IsEqualTo("original");
	}

	[Test]
	public async Task Build_InvalidNewOptions_FailsBeforeBackendCreation()
	{
		// Arrange
		var missingName = new TestBuilder().WithImage("alpine").WithReuse();
		var missingNetwork = new TestBuilder().WithImage("alpine").WithNetworkAliases("peer");
		var invalidTimeout = new TestBuilder().WithImage("alpine").WithExecTimeout(Timeout.InfiniteTimeSpan);

		// Act / Assert
		await Assert.That(() => missingName.Build()).Throws<ContainerConfigurationException>();
		await Assert.That(() => missingNetwork.Build()).Throws<ContainerConfigurationException>();
		await Assert.That(() => invalidTimeout.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	[Arguments(-1d)]
	[Arguments(0d)]
	[Arguments(4294967295d)]
	public async Task OperationOptions_InvalidDeadline_RejectsInfiniteOrNonPositiveWaits(double milliseconds)
	{
		// Arrange
		var timeout = TimeSpan.FromMilliseconds(milliseconds);

		// Act / Assert
		await Assert.That(() => new ExecOptions { Timeout = timeout }).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => new NetworkConfiguration { Timeout = timeout }).Throws<ArgumentOutOfRangeException>();
		await Assert
			.That(() =>
				new ImageBuildConfiguration
				{
					ContextDirectory = ".",
					Name = "unused",
					Timeout = timeout,
				}
			)
			.Throws<ArgumentOutOfRangeException>();
	}

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
		await Assert.That(() => new TestBuilder().Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task CreateContainer_WithUdpPort_ThrowsNotSupported()
	{
		// UDP is a backend capability, so the neutral builder accepts the configuration and the WSL
		// Containers backend rejects it (the Docker backend supports UDP).
		var configuration = new TestBuilder()
			.WithImage("alpine")
			.WithPortBinding(53, 53, PortProtocol.Udp)
			.BuildConfig();

		await Assert
			.That(() => new WslContainerBackend().CreateContainer(configuration))
			.Throws<ContainerNotSupportedException>();
	}

	[Test]
	public async Task Build_WithContainerPortZero_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithPortBinding(0, assignRandomHostPort: true);
		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithHostPortZero_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithPortBinding(80, 0);
		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithEmptyBindMount_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithBindMount(string.Empty, "/data");
		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task Build_WithZeroStartupTimeout_Throws()
	{
		var builder = new TestBuilder().WithImage("alpine").WithStartupTimeout(TimeSpan.Zero);
		await Assert.That(() => builder.Build()).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task GenericContainerBuilder_GeneratesUniqueName()
	{
		var first = new ContainerBuilder(new WslContainerBackend()).WithImage("alpine").Build();
		var second = new ContainerBuilder(new WslContainerBackend()).WithImage("alpine").Build();

		await Assert.That(first.Name).IsNotEqualTo(second.Name);
		await Assert.That(first.Image).IsEqualTo("docker.io/library/alpine:latest");
	}

	[Test]
	public async Task WithImage_InvalidReference_ThrowsImmediately()
	{
		await Assert
			.That(() => new ContainerBuilder().WithImage("bad image name"))
			.Throws<ContainerConfigurationException>();
		await Assert
			.That(() => new ContainerBuilder().WithImage(string.Empty))
			.Throws<ContainerConfigurationException>();
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
		await Assert.That(() => new ContainerBuilder().WithTag("7.4")).Throws<ContainerConfigurationException>();
	}

	[Test]
	public async Task WithTag_InvalidTag_Throws()
	{
		await Assert
			.That(() => new ContainerBuilder().WithImage("redis").WithTag("bad tag"))
			.Throws<ContainerConfigurationException>();
	}
}
