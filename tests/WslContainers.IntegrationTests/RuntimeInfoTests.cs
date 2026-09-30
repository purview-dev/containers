using Purview.WslContainers;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.IntegrationTests;

public class RuntimeInfoTests
{
	[Test]
	public async Task GetInfoAsync_ReportsAvailability()
	{
		await WslcTest.SkipIfUnavailableAsync();

		WslContainerRuntimeInfo info = await WslContainerRuntime.Instance.GetInfoAsync();

		await Assert.That(info.IsAvailable).IsTrue();
		await Assert.That(info.IsCompatible).IsTrue();
		await Assert.That(info.Version).IsNotEmpty();
	}
}
