using System.Diagnostics;

namespace Purview.Containers.Wsl.Diagnostics;

/// <summary>Activity source for container operations. Listener names: <c>Purview.Containers</c>.</summary>
static class WslContainerActivity
{
	/// <summary>Activities: session.start, image.pull, container.create/start/stop/delete, exec.create, wait.</summary>
	public static readonly ActivitySource Source = new("Purview.Containers");
}
