using TUnit.Core.Exceptions;

namespace Purview.WslContainers.MinIO;

class MinIOIntegrationTests
{
	[Test]
	public async Task MinIO_ExposesAPIAndConsole()
	{
		await using var minio = await StartOrSkipAsync();

		await WslcTest.WaitForTcpAsync(minio.GetMappedPublicPort(MinIOBuilder.APIPort), TimeSpan.FromSeconds(30));
		await WslcTest.WaitForTcpAsync(minio.GetMappedPublicPort(MinIOBuilder.ConsolePort), TimeSpan.FromSeconds(30));

		using HttpClient client = new();
		using var response = await client.GetAsync(minio.GetConsoleEndpoint());
		await Assert.That((int)response.StatusCode).IsEqualTo(200);
	}

	[Test]
	public async Task Endpoints_UseMappedRandomPorts()
	{
		await using var minio = await StartOrSkipAsync();

		var apiPort = minio.GetMappedPublicPort(MinIOBuilder.APIPort);
		var consolePort = minio.GetMappedPublicPort(MinIOBuilder.ConsolePort);

		await Assert.That(minio.GetAPIEndpoint().ToString()).IsEqualTo($"http://127.0.0.1:{apiPort}");
		await Assert.That(minio.GetConsoleEndpoint().ToString()).IsEqualTo($"http://127.0.0.1:{consolePort}");
		await Assert.That(minio.GetAccessKey()).IsEqualTo("minioadmin");
	}

	// MinIO's official image lives on quay.io, which some networks/WSL sessions cannot reach
	// (e.g. IPv6-only resolution with no route). Skip gracefully rather than fail the suite.
	static async Task<MinIOContainer> StartOrSkipAsync()
	{
		await WslcTest.SkipIfUnavailableAsync();

		var minio = new MinIOBuilder().Build();
		try
		{
			await minio.StartAsync();
			return minio;
		}
		catch (Exception ex)
		{
			await minio.DisposeAsync();
			var message = ex.ToString();
			if (
				message.Contains("network is unreachable", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
			)
			{
				throw new SkipTestException($"MinIO image could not be pulled from its registry: {ex.Message}");
			}

			throw;
		}
	}
}
