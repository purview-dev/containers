namespace Purview.Containers.Wsl;

public class WslContainerResidueTests
{
	[Test]
	[Arguments("server-999999-abcdef", true)]
	[Arguments("rabbitmq-999999-0badc0", true)]
	[Arguments("server-0-abcdef", false)]
	[Arguments("server-999999-ABCDEF", false)]
	[Arguments("server-999999-abcdef12", false)]
	[Arguments("diag-root", false)]
	[Arguments("my-container", false)]
	public async Task IsStaleOwnedContainer_OnlyAcceptsGeneratedNamesWithDeadPids(string name, bool expected)
	{
		var result = WslContainerResidue.IsStaleOwnedContainer(name);
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	public async Task IsStaleOwnedContainer_DoesNotRemoveTheCurrentProcessContainer()
	{
		var current = Environment.ProcessId;
		var result = WslContainerResidue.IsStaleOwnedContainer($"server-{current}-abcdef");
		await Assert.That(result).IsFalse();
	}
}
