using System.Diagnostics;

namespace Purview.WslContainers;

/// <summary>Activity source for WSL Containers operations. Listener names: <c>Purview.WslContainers</c>.</summary>
internal static class WslContainersActivity
{
	/// <summary>Activities: session.start, image.pull, container.create/start/stop/delete, exec.create, wait.</summary>
	public static readonly ActivitySource Source = new ActivitySource("Purview.WslContainers");
}
