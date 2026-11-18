namespace Purview.Containers.Docker;

static class DockerFileCopy
{
	internal static async Task CopyAsync(
		string id,
		string source,
		string target,
		UnixFileMode? mode,
		CancellationToken cancellationToken
	)
	{
		using var archive = await ContainerResourceArchive
			.CreateAsync(source, target, mode, cancellationToken)
			.ConfigureAwait(false);
		using var client = DockerEndpoint.CreateClient();
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
