namespace Purview.Containers;

/// <summary>
/// Optional backend metadata consulted by automatic backend selection.
/// </summary>
/// <remarks>
/// <para>
/// When several registered backends are usable, <see cref="ContainerBackends.ResolveAsync" /> probes them
/// in <see cref="AutoPriority" /> order (lowest first) and returns the first usable one. A backend that
/// does not implement this interface is treated as priority <c>0</c>, and equal priorities keep
/// registration order, so this is purely additive: existing backends behave exactly as before.
/// </para>
/// <para>
/// It exists so a documented preference — "on a machine that can run both, prefer WSL Containers over
/// Docker" — is expressed by the backends themselves rather than by the order NuGet happens to import
/// their <c>buildTransitive</c> props. A pinned instance or a named selection always overrides it.
/// </para>
/// </remarks>
public interface IContainerBackendPreference
{
	/// <summary>
	/// Preference for automatic selection: a lower value wins. Only consulted for automatic
	/// (<c>auto</c>) selection.
	/// </summary>
	int AutoPriority { get; }
}
