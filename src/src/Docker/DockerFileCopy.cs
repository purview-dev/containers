using DotNet.Testcontainers.Configurations;

namespace Purview.Containers.Docker;

static class DockerFileCopy
{
	internal static async Task CopyAsync(string id, string source, string target, CancellationToken cancellationToken)
	{
		using var archive = await ContainerResourceArchive
			.CreateAsync(source, target, cancellationToken)
			.ConfigureAwait(false);
		using var client = TestcontainersSettings
			.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid())
			.Build();
		await client
			.Containers.ExtractArchiveToContainerAsync(
				id,
				new global::Docker.DotNet.Models.CopyToContainerParameters { Path = "/" },
				archive,
				cancellationToken
			)
			.ConfigureAwait(false);
	}
}
