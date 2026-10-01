using System.Diagnostics;

namespace Purview.Containers.Diagnostics;

/// <summary>
/// Activity source for backend-neutral container operations. Listener names:
/// <c>Purview.Containers</c> (the WSL Containers backend emits to the same name).
/// </summary>
static class ContainerActivity
{
	/// <summary>Activities: wait.</summary>
	public static readonly ActivitySource Source = new("Purview.Containers");
}
