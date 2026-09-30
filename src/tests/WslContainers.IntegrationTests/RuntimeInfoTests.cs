namespace Purview.WslContainers;

[Explicit]
public class RuntimeInfoTests
{
	[Test]
	public async Task GetInfoAsync_ReportsAvailability()
	{
		await WslcTest.SkipIfUnavailableAsync();

		var info = await WslContainerRuntime.Instance.GetInfoAsync();

		await Assert.That(info.IsAvailable).IsTrue();
		await Assert.That(info.IsCompatible).IsTrue();
		await Assert.That(info.Version).IsNotEmpty();
	}
}
