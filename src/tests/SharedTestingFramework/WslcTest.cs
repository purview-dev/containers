using Purview.Containers.Wsl;
using TUnit.Core.Exceptions;

namespace Purview.Containers;

/// <summary>Shared helpers for integration tests that exercise real WSL Containers.</summary>
public static class WslcTest
{
	static int BackendRegistered;

	/// <summary>
	/// Registers the WSL Containers backend. Package consumers get this from the generated module
	/// initializer in <c>Purview.Containers.Backends.targets</c>; these test projects reference the
	/// backend as a project, so they register it explicitly here. Idempotent.
	/// </summary>
	public static void EnsureBackendRegistered()
	{
		if (Interlocked.Exchange(ref BackendRegistered, 1) == 0)
		{
			ContainerBackends.Register(WslContainerBackend.Create());
		}
	}

	/// <summary>Skips the current test when WSL Containers prerequisites are missing.</summary>
	public static async Task SkipIfUnavailableAsync()
	{
		EnsureBackendRegistered();
		var info = await WslContainerRuntime.Instance.GetInfoAsync();
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
		using System.Net.Sockets.TcpClient client = new();
		using CancellationTokenSource timeoutSource = new(timeout);
		await client.ConnectAsync(System.Net.IPAddress.Loopback, port, timeoutSource.Token);
	}
}
