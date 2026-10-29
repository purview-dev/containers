using System.Collections.Concurrent;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.Containers.Environment;

#pragma warning disable IDE0130 // Aspire extension methods belong beside Aspire's own builder extensions.
namespace Aspire.Hosting;

/// <summary>Adapts resource bind mounts without depending on any particular Aspire integration.</summary>
public static class ContainerEnvironmentBuilderExtensions
{
	/// <summary>
	/// Verifies and resolves bind mounts immediately before containers start. Named volumes, target
	/// paths, read-only flags, resource endpoints and publish manifests are left unchanged.
	/// </summary>
	public static IDistributedApplicationBuilder AddContainerEnvironment(
		this IDistributedApplicationBuilder builder,
		ContainerEnvironmentResolver? environment = null
	)
	{
		ArgumentNullException.ThrowIfNull(builder);
		if (builder.Services.Any(service => service.ServiceType == typeof(ContainerEnvironmentRegistration)))
			return builder;

		builder.Services.TryAddSingleton<ContainerEnvironmentRegistration>();
		environment ??= ContainerEnvironmentResolver.ForRuntime(
			builder.Configuration["ASPIRE_CONTAINER_RUNTIME"] ?? "docker"
		);
		ConcurrentDictionary<ContainerMountAnnotation, string> originalSources = new(
			ReferenceEqualityComparer.Instance
		);

		builder.Eventing.Subscribe<BeforeResourceStartedEvent>(
			async (evt, cancellationToken) =>
			{
				// This event runs after BeforeStart subscribers add mounts, but before DCP renders them.
				foreach (var mount in evt.Resource.Annotations.OfType<ContainerMountAnnotation>().ToArray())
				{
					if (mount.Type != ContainerMountType.BindMount || mount.Source is null)
						continue;

					var original = originalSources.GetOrAdd(mount, mount.Source);
					var source = await environment
						.ResolveBindSourceAsync(original, cancellationToken)
						.ConfigureAwait(false);
					if (source == mount.Source)
						continue;

					ContainerMountAnnotation replacement = new(source, mount.Target, mount.Type, mount.IsReadOnly);
					originalSources[replacement] = original;
					evt.Resource.Annotations.Remove(mount);
					evt.Resource.Annotations.Add(replacement);
					originalSources.TryRemove(mount, out _);
				}
			}
		);

		return builder;
	}

	sealed class ContainerEnvironmentRegistration;
}
