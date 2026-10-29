using System.Formats.Tar;
using Purview.Containers.Runtime;

namespace Purview.Containers;

static class ContainerResourceArchive
{
	internal static async Task<MemoryStream> CreateAsync(
		string source,
		string target,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(target);
		cancellationToken.ThrowIfCancellationRequested();
		if (!target.StartsWith('/') || target.Split('/').Contains("..", StringComparer.Ordinal))
			throw new ContainerConfigurationException("Copy targets must be absolute container paths without '..'.");

		MemoryStream archive = new();
		try
		{
			using (TarWriter writer = new(archive, leaveOpen: true))
			{
				if (Directory.Exists(source))
				{
					if (target.Trim('/').Length > 0)
						await writer
							.WriteEntryAsync(
								new PaxTarEntry(TarEntryType.Directory, target.Trim('/'))
								{
									Mode =
										UnixFileMode.UserRead
										| UnixFileMode.UserWrite
										| UnixFileMode.UserExecute
										| UnixFileMode.GroupRead
										| UnixFileMode.GroupExecute
										| UnixFileMode.OtherRead
										| UnixFileMode.OtherExecute,
								},
								cancellationToken
							)
							.ConfigureAwait(false);
					foreach (
						var entry in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)
					)
					{
						cancellationToken.ThrowIfCancellationRequested();
						var relative = Path.GetRelativePath(source, entry).Replace('\\', '/');
						await writer
							.WriteEntryAsync(entry, $"{target.Trim('/')}/{relative}", cancellationToken)
							.ConfigureAwait(false);
					}
				}
				else
					await writer.WriteEntryAsync(source, target.Trim('/'), cancellationToken).ConfigureAwait(false);
			}
			archive.Position = 0;
			return archive;
		}
		catch
		{
			await archive.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}
}
