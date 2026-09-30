using Purview.WslContainers;
using TUnit.Core.Exceptions;

namespace Purview.WslContainers.Testing;

/// <summary>Shared helpers for integration tests that exercise real WSL Containers.</summary>
public static class WslcTest
{
	/// <summary>Skips the current test when WSL Containers prerequisites are missing.</summary>
	public static async Task SkipIfUnavailableAsync()
	{
		WslContainerRuntimeInfo info = await WslContainerRuntime.Instance.GetInfoAsync();
		if (!info.IsAvailable || !info.IsCompatible)
		{
			throw new SkipTestException(
				$"WSL Containers unavailable. Missing: {string.Join(", ", info.MissingComponents)}"
			);
		}
	}

	/// <summary>Connects to a host TCP port, throwing when the timeout elapses.</summary>
	public static async Task WaitForTcpAsync(int port, TimeSpan timeout)
	{
		using System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient();
		using System.Threading.CancellationTokenSource timeoutSource = new System.Threading.CancellationTokenSource(
			timeout
		);
		await client.ConnectAsync(System.Net.IPAddress.Loopback, port, timeoutSource.Token);
	}
}
