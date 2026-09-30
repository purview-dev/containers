namespace Purview.WslContainers;

public class StorageModeTests
{
	[Test]
	public async Task DefaultOptions_UseSharedStorage()
	{
		var options = WslContainerRuntimeOptions.Default;

		await Assert.That(options.StorageMode).IsEqualTo(StorageMode.Shared);
		await Assert.That(options.StoragePath).IsNull();
	}

	[Test]
	public async Task PerSessionStorage_IsSelectable()
	{
		WslContainerRuntimeOptions options = new() { StorageMode = StorageMode.PerSession };

		await Assert.That(options.StorageMode).IsEqualTo(StorageMode.PerSession);
	}

	[Test]
	public async Task ExplicitStoragePath_OverridesSharedDefault()
	{
		WslContainerRuntimeOptions options = new() { StoragePath = @"C:\temp\store" };

		await Assert.That(options.StoragePath).IsEqualTo(@"C:\temp\store");
		await Assert.That(options.StorageMode).IsEqualTo(StorageMode.Shared);
	}
}
