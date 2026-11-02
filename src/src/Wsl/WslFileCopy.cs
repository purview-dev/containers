using System.Formats.Tar;

namespace Purview.Containers.Wsl;

static class WslFileCopy
{
	internal static async Task CopyAsync(
		WslContainerSession session,
		string id,
		string source,
		string target,
		CancellationToken cancellationToken
	)
	{
		using var archive = await ContainerResourceArchive
			.CreateAsync(source, target, cancellationToken)
			.ConfigureAwait(false);
		await session.RunCopyCommandAsync(["cp", "-", $"{id}:/"], cancellationToken, archive).ConfigureAwait(false);
	}

	internal static async Task<byte[]> ReadAsync(
		WslContainerSession session,
		string id,
		string path,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		var result = await session
			.RunCopyCommandAsync(["cp", $"{id}:{path}", "-"], cancellationToken)
			.ConfigureAwait(false);
		using MemoryStream archive = new(result.Output);
		using TarReader reader = new(archive);
		var entry = await reader.GetNextEntryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
		if (entry is { EntryType: TarEntryType.RegularFile or TarEntryType.V7RegularFile, DataStream: { } data })
		{
			using MemoryStream output = new();
			await data.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
			return output.ToArray();
		}
		throw new FileNotFoundException("The container path did not contain a regular file.");
	}
}
