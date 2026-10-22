using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<Purview.Containers.Wsl.WslIntegrationParallelLimit>]

namespace Purview.Containers.Wsl;

/// <summary>
/// Caps how many WSLC integration tests run at once. Every test in this assembly starts real
/// containers in the one process-wide session, and WSLC serialises the session operations anyway, so
/// letting all 27 tests start containers together only adds image-pull and memory contention to the
/// 4 GB session VM. Two keeps some overlap without making readiness timeouts a lottery.
/// </summary>
public sealed record WslIntegrationParallelLimit : IParallelLimit
{
	/// <inheritdoc />
	public int Limit => 2;
}
