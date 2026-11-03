using System.Collections.Concurrent;
using System.Net;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using Purview.Containers.Mounts;

namespace Purview.Containers.Docker;

/// <summary>
/// Owns the named Docker volumes this backend created and, when cleanup is enabled, removes them (and the
/// containers that used them) on process exit or on an explicit backend cleanup.
/// </summary>
sealed class DockerVolumeCleanup
{
	const string OwnerLabel = "purview.containers.owner";
	const string CleanupLabel = "purview.containers.cleanup";
	const string OwnerValue = "volume";

	static readonly ConcurrentDictionary<DockerVolumeCleanup, byte> Trackers = new();
	static int HookRegistered;

	readonly ConcurrentDictionary<string, byte> _volumes = new(StringComparer.Ordinal);
	readonly ConcurrentDictionary<string, byte> _containers = new(StringComparer.Ordinal);

	/// <summary>
	/// Creates the named volume when it does not already exist, so only volumes created by this backend are
	/// ever deleted. An existing volume is left unowned.
	/// </summary>
	internal async Task EnsureOwnedAsync(IReadOnlyList<NamedVolume> volumes, CancellationToken cancellationToken)
	{
		if (!ContainerCleanup.CleanUpVolumesOnExit || volumes.Count == 0)
		{
			return;
		}

		Trackers[this] = 0;
		EnsureProcessExitHook();
		using var client = CreateClient();
		foreach (var volume in volumes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				await client
					.Volumes.CreateAsync(
						new VolumesCreateParameters
						{
							Name = volume.Name,
							Labels = new Dictionary<string, string>(StringComparer.Ordinal)
							{
								[OwnerLabel] = OwnerValue,
								[CleanupLabel] = "true",
							},
						},
						cancellationToken
					)
					.ConfigureAwait(false);

				// Docker returns an existing volume unchanged, so only a volume carrying our labels is ours.
				var created = await client.Volumes.InspectAsync(volume.Name, cancellationToken).ConfigureAwait(false);
				if (
					created.Labels is { } labels
					&& labels.TryGetValue(OwnerLabel, out var owner)
					&& string.Equals(owner, OwnerValue, StringComparison.Ordinal)
					&& labels.TryGetValue(CleanupLabel, out var cleanup)
					&& string.Equals(cleanup, "true", StringComparison.Ordinal)
				)
				{
					_volumes[volume.Name] = 0;
				}
			}
			catch (global::Docker.DotNet.DockerApiException)
			{
				// Let container creation report the underlying driver or permission problem.
			}
		}
	}

	/// <summary>Records a container so cleanup can force-remove it before deleting its volumes.</summary>
	internal void RegisterContainer(string id)
	{
		if (!ContainerCleanup.CleanUpVolumesOnExit || string.IsNullOrEmpty(id))
		{
			return;
		}

		Trackers[this] = 0;
		EnsureProcessExitHook();
		_containers[id] = 0;
	}

	internal async Task CleanUpAsync(CancellationToken cancellationToken = default)
	{
		if (_volumes.IsEmpty && _containers.IsEmpty)
		{
			return;
		}

		using var client = CreateClient();
		foreach (var id in _containers.Keys)
		{
			try
			{
				await client
					.Containers.RemoveContainerAsync(
						id,
						new ContainerRemoveParameters { Force = true, RemoveVolumes = false },
						cancellationToken
					)
					.ConfigureAwait(false);
			}
			catch (global::Docker.DotNet.DockerApiException exception)
				when (exception.StatusCode == HttpStatusCode.NotFound)
			{
				// Already removed by Testcontainers, the caller, or the resource reaper.
			}
			catch (global::Docker.DotNet.DockerApiException)
			{
				// Best-effort: a volume still in use is reported by the volume removal below.
			}
		}

		_containers.Clear();
		foreach (var name in _volumes.Keys)
		{
			try
			{
				await client.Volumes.RemoveAsync(name, force: true, cancellationToken).ConfigureAwait(false);
			}
			catch (global::Docker.DotNet.DockerApiException)
			{
				// Best-effort cleanup; never fail the caller for a volume that is already gone or in use.
			}
		}

		_volumes.Clear();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "Process-exit cleanup is best-effort; the process is already terminating."
	)]
	static void EnsureProcessExitHook()
	{
		if (Interlocked.Exchange(ref HookRegistered, 1) == 1)
		{
			return;
		}

		AppDomain.CurrentDomain.ProcessExit += (_, _) =>
		{
			if (!ContainerCleanup.CleanUpVolumesOnExit)
			{
				return;
			}

			using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
			foreach (var tracker in Trackers.Keys)
			{
				try
				{
					tracker.CleanUpAsync(deadline.Token).GetAwaiter().GetResult();
				}
				catch { }
			}
		};
	}

	static global::Docker.DotNet.DockerClient CreateClient() =>
		TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid()).Build();
}
