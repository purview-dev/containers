using global::Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // Tests share the extension class's Aspire.Hosting namespace.
namespace Aspire.Hosting;

public sealed class ContainerEnvironmentBuilderExtensionsTests
{
	static IDistributedApplicationBuilder CreateBuilder() => DistributedApplication.CreateBuilder([]);

	static ContainerEnvironmentResolver CreateEnvironment() =>
		new(
			new ContainerEnvironmentOptions { PathMappings = new() { [Path.GetTempPath()] = "/daemon/temp" } },
			(_, _, _) => Task.FromResult(true),
			(_, _) => Task.FromResult<IReadOnlyList<string>>([])
		);

	[Test]
	public async Task AddContainerEnvironment_ResourceStartsTwice_PreservesMountSemantics(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var builder = CreateBuilder();
		using var environment = CreateEnvironment();
		builder.AddContainerEnvironment(environment);
		builder.AddContainerEnvironment(environment);
		var resource = builder
			.AddContainer("example", "example")
			.WithBindMount(Path.GetTempPath(), "/data", isReadOnly: true)
			.WithVolume("cache", "/cache");
		using var services = builder.Services.BuildServiceProvider();

		// Act
		await builder.Eventing.PublishAsync(
			new BeforeResourceStartedEvent(resource.Resource, services),
			cancellationToken
		);
		await builder.Eventing.PublishAsync(
			new BeforeResourceStartedEvent(resource.Resource, services),
			cancellationToken
		);
		var mounts = resource.Resource.Annotations.OfType<ContainerMountAnnotation>().ToArray();

		// Assert
		await Assert.That(mounts.Length).IsEqualTo(2);
		var bind = mounts.Single(mount => mount.Type == ContainerMountType.BindMount);
		await Assert.That(bind.Source).IsEqualTo("/daemon/temp");
		await Assert.That(bind.Target).IsEqualTo("/data");
		await Assert.That(bind.IsReadOnly).IsTrue();
		await Assert.That(mounts.Single(mount => mount.Type == ContainerMountType.Volume).Source).IsEqualTo("cache");
	}
}
