using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<Purview.Containers.Docker.DockerIntegrationParallelLimit>]

namespace Purview.Containers.Docker;

/// <summary>
/// Caps how many Docker integration tests run at once. Testcontainers starts one process-wide
/// resource reaper (Ryuk); concurrent first starts can exceed its one-minute init timeout even when
/// only two tests race, and a 2-core CI runner tolerates very little burst. The suite is fast, so run
/// one test at a time and keep the reaper initialisation uncontended.
/// </summary>
public sealed record DockerIntegrationParallelLimit : IParallelLimit
{
	/// <inheritdoc />
	public int Limit => 1;
}
