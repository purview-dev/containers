using Purview.WslContainers;
using Purview.WslContainers.Images;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.IntegrationTests;

public class SharedStorageTests
{
	[Test]
	public async Task SharedStore_IsReusedAcrossSequentialRuntimes()
	{
		await WslcTest.SkipIfUnavailableAsync();

		string path = Path.Combine(Path.GetTempPath(), "wslc-shared-" + Guid.NewGuid().ToString("N")[..6]);
		Directory.CreateDirectory(path);
		try
		{
			IReadOnlyList<ImageSummary> firstRun;
			await using (WslContainerRuntime runtime1 = new WslContainerRuntime(new WslContainerRuntimeOptions
			{
				StoragePath = path,
				DisableProcessExitCleanup = true,
			}))
			{
				await using WslContainer container = new ContainerBuilder(runtime1)
					.WithImage("alpine:latest")
					.WithCommand("/bin/echo", "one")
					.Build();
				await container.StartAsync();
				IContainerSession session1 = await runtime1.GetSessionAsync();
				firstRun = await session1.ListImagesAsync();
			}

			IReadOnlyList<ImageSummary> secondRun;
			await using (WslContainerRuntime runtime2 = new WslContainerRuntime(new WslContainerRuntimeOptions
			{
				StoragePath = path,
				DisableProcessExitCleanup = true,
			}))
			{
				IContainerSession session2 = await runtime2.GetSessionAsync();
				secondRun = await session2.ListImagesAsync();
			}

			await Assert.That(firstRun.Any(image => image.Name.Contains("alpine", StringComparison.OrdinalIgnoreCase))).IsTrue();
			await Assert.That(secondRun.Any(image => image.Name.Contains("alpine", StringComparison.OrdinalIgnoreCase))).IsTrue();
		}
		finally
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// VHD may still be held briefly by the session manager.
			}
			catch (UnauthorizedAccessException)
			{
				// Same.
			}
		}
	}
}