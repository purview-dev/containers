namespace Purview.WslContainers.Containers;

/// <summary>Lifecycle state of a WSLC container.</summary>
public enum ContainerState
{
	/// <summary>The container handle is invalid.</summary>
	Invalid = 0,

	/// <summary>The container has been created but not started.</summary>
	Created = 1,

	/// <summary>The container init process is running.</summary>
	Running = 2,

	/// <summary>The container init process has exited.</summary>
	Exited = 3,

	/// <summary>The container has been deleted.</summary>
	Deleted = 4,
}
