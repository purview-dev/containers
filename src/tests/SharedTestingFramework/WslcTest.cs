using Purview.Containers.Wsl;
using TUnit.Core.Exceptions;

namespace Purview.Containers;

/// <summary>Shared helpers for integration tests that exercise real WSL Containers.</summary>
public static class WslcTest
{
	/// <summary>
	/// Set to <c>wsl</c> to make an unavailable or incompatible WSLC host fail the test run instead of
	/// skipping it. CI sets this for the WSLC jobs so a mis-provisioned runner cannot go green with
	/// zero coverage.
	/// </summary>
	public const string RequireBackendEnvironmentVariable = "PURVIEW_CONTAINERS_REQUIRE_BACKEND";

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
			var reason = $"WSL Containers unavailable. Missing: {string.Join(", ", info.MissingComponents)}";
			if (IsBackendRequired())
			{
				throw new InvalidOperationException(
					$"{reason} {RequireBackendEnvironmentVariable}=wsl requires a usable WSLC host."
				);
			}

			throw new SkipTestException(reason);
		}
	}

	/// <summary>
	/// Connects to a host TCP port, retrying until <paramref name="timeout" /> elapses. A refused
	/// connection fails immediately, so a single <c>ConnectAsync</c> is not a wait.
	/// </summary>
	public static async Task WaitForTcpAsync(int port, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		Exception? lastError = null;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using System.Net.Sockets.TcpClient client = new();
				using CancellationTokenSource attemptSource = new(TimeSpan.FromSeconds(2));
				await client.ConnectAsync(System.Net.IPAddress.Loopback, port, attemptSource.Token);
				return;
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				lastError = exception;
			}
			catch (OperationCanceledException)
			{
				// A per-attempt connect timeout is just another not-yet-listening result.
			}

			await Task.Delay(TimeSpan.FromMilliseconds(250));
		}

		throw new System.TimeoutException(
			$"TCP port {port} did not become reachable within {timeout}. Last error: "
				+ $"{lastError?.GetType().Name}: {lastError?.Message}"
		);
	}

	static bool IsBackendRequired() =>
		string.Equals(
			Environment.GetEnvironmentVariable(RequireBackendEnvironmentVariable),
			"wsl",
			StringComparison.OrdinalIgnoreCase
		);
}
