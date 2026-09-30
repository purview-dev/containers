using Purview.WslContainers.MinIo;
using Purview.WslContainers.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core.Exceptions;

namespace WslContainers.MinIo.IntegrationTests;

public class MinIoIntegrationTests
{
	[Test]
	public async Task MinIo_ExposesApiAndConsole()
	{
		await using MinIoContainer minio = await StartOrSkipAsync();

		await WslcTest.WaitForTcpAsync(minio.GetMappedPublicPort(MinIoBuilder.ApiPort), TimeSpan.FromSeconds(30));
		await WslcTest.WaitForTcpAsync(minio.GetMappedPublicPort(MinIoBuilder.ConsolePort), TimeSpan.FromSeconds(30));

		using HttpClient client = new HttpClient();
		using HttpResponseMessage response = await client.GetAsync(minio.GetConsoleEndpoint());
		await Assert.That((int)response.StatusCode).IsEqualTo(200);
	}

	[Test]
	public async Task Endpoints_UseMappedRandomPorts()
	{
		await using MinIoContainer minio = await StartOrSkipAsync();

		ushort apiPort = minio.GetMappedPublicPort(MinIoBuilder.ApiPort);
		ushort consolePort = minio.GetMappedPublicPort(MinIoBuilder.ConsolePort);

		await Assert.That(minio.GetApiEndpoint().ToString()).IsEqualTo($"http://127.0.0.1:{apiPort}");
		await Assert.That(minio.GetConsoleEndpoint().ToString()).IsEqualTo($"http://127.0.0.1:{consolePort}");
		await Assert.That(minio.GetAccessKey()).IsEqualTo("minioadmin");
	}

	// MinIO's official image lives on quay.io, which some networks/WSL sessions cannot reach
	// (e.g. IPv6-only resolution with no route). Skip gracefully rather than fail the suite.
	private static async Task<MinIoContainer> StartOrSkipAsync()
	{
		await WslcTest.SkipIfUnavailableAsync();

		MinIoContainer minio = new MinIoBuilder().Build();
		try
		{
			await minio.StartAsync();
			return minio;
		}
		catch (Exception ex)
		{
			await minio.DisposeAsync();
			string message = ex.ToString();
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
