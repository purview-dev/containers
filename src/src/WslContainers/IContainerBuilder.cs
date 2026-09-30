namespace Purview.WslContainers;

/// <summary>Produces an immutable <see cref="IContainer" /> from fluent configuration.</summary>
public interface IContainerBuilder<out TContainer>
	where TContainer : IContainer
{
	/// <summary>Validates the configuration and builds a container.</summary>
	TContainer Build();
}
