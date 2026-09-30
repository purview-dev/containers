using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.WSL.Containers;

namespace WslcSpikes;

// S3: stdout/stderr capture and exit codes for the init process.
static class ProcessIo
{
	public static async Task<int> RunAsync()
	{
		Console.WriteLine("=== S3: init-process stdout/stderr/exit ===");
		Session session = SpikeSupport.StartSession("wslcspike-s3");
		try
		{
			await SpikeSupport.PullImageAsync(session, "docker.io/library/alpine:latest");

			Console.WriteLine("[s3] case 1: echo to stdout, exit 0");
			Container c1 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s3-echo"),
				new[] { "/bin/echo", "hello-stdout" }
			);
			await SpikeSupport.RunToExitAsync(c1, TimeSpan.FromSeconds(30));
			c1.Delete(DeleteContainerOption.None);
			c1.Dispose();

			Console.WriteLine("[s3] case 2: stdout + stderr, exit 7");
			Container c2 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s3-mixed"),
				new[] { "/bin/sh", "-c", "echo out-line; echo err-line >&2; exit 7" }
			);
			await SpikeSupport.RunToExitAsync(c2, TimeSpan.FromSeconds(30));
			c2.Delete(DeleteContainerOption.None);
			c2.Dispose();

			Console.WriteLine("[s3] case 3: missing executable (start should surface failure)");
			Container c3 = SpikeSupport.CreateContainer(
				session,
				"docker.io/library/alpine:latest",
				SpikeSupport.UniqueName("s3-missing"),
				new[] { "/bin/definitely-not-a-binary" }
			);
			try
			{
				await SpikeSupport.RunToExitAsync(c3, TimeSpan.FromSeconds(30));
			}
			catch (Exception ex)
			{
				SpikeSupport.Dump(ex);
			}

			c3.Delete(DeleteContainerOption.Force);
			c3.Dispose();
		}
		finally
		{
			SpikeSupport.Cleanup(session);
		}

		Console.WriteLine("[s3] S3 complete.");
		return 0;
	}
}

static class HttpSupport
{
	public const string ServerImage = "docker.io/library/python:3-alpine";

	public static IReadOnlyList<string> ServerArgs(int port) =>
		new[] { "/bin/sh", "-c", $"python3 -m http.server {port} -d /tmp" };

	public static async Task<HttpResponseMessage> GetAsync(int port, string path = "/", string host = "127.0.0.1")
	{
		using HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
		client.Timeout = TimeSpan.FromSeconds(5);
		return await client.GetAsync($"http://{host}:{port}{path}").ConfigureAwait(false);
	}

	public static ushort GetFreeTcpPort()
	{
		TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	public static async Task<bool> WaitForTcpAsync(int port, TimeSpan timeout, string host = "127.0.0.1")
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using TcpClient client = new TcpClient();
				await client.ConnectAsync(host, port).ConfigureAwait(false);
				return true;
			}
			catch (SocketException)
			{
				await Task.Delay(200).ConfigureAwait(false);
			}
		}

		return false;
	}

	public static async Task<bool> WaitForTcp6Async(int port, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using TcpClient client = new TcpClient(AddressFamily.InterNetworkV6);
				await client.ConnectAsync(IPAddress.IPv6Loopback, port).ConfigureAwait(false);
				return true;
			}
			catch (SocketException)
			{
				await Task.Delay(200).ConfigureAwait(false);
			}
		}

		return false;
	}

	public static int? TryReadAssignedHostPort(string json)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			return FindHostPort(doc.RootElement);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static string? TryReadContainerIp(string json, string network = "bridge")
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			if (
				doc.RootElement.TryGetProperty("NetworkSettings", out JsonElement networkSettings)
				&& networkSettings.TryGetProperty("Networks", out JsonElement networks)
				&& networks.TryGetProperty(network, out JsonElement net)
				&& net.TryGetProperty("IPAddress", out JsonElement ip)
				&& ip.ValueKind == JsonValueKind.String
			)
			{
				string value = ip.GetString() ?? string.Empty;
				return string.IsNullOrEmpty(value) ? null : value;
			}

			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static int? FindHostPort(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in element.EnumerateObject())
			{
				string key = property.Name;
				if (
					key.Equals("HostPort", StringComparison.OrdinalIgnoreCase)
					|| key.Equals("WindowsPort", StringComparison.OrdinalIgnoreCase)
					|| key.Equals("PublicPort", StringComparison.OrdinalIgnoreCase)
				)
				{
					int? value = property.Value.ValueKind switch
					{
						JsonValueKind.Number when property.Value.TryGetInt32(out int n) => n,
						JsonValueKind.String when int.TryParse(property.Value.GetString(), out int s) => s,
						_ => null,
					};
					if (value is int parsed && parsed > 0)
					{
						return parsed;
					}
				}

				int? nested = FindHostPort(property.Value);
				if (nested is not null)
				{
					return nested;
				}
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				int? nested = FindHostPort(item);
				if (nested is not null)
				{
					return nested;
				}
			}
		}

		return null;
	}

	public static async Task<(int Status, string Body)> GetWithRetryAsync(int port, string path = "/", int attempts = 8)
	{
		for (int i = 0; i < attempts; i++)
		{
			try
			{
				using System.Net.Http.HttpResponseMessage resp = await GetAsync(port, path).ConfigureAwait(false);
				string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
				return ((int)resp.StatusCode, body);
			}
			catch (System.Net.Http.HttpRequestException)
			{
				await Task.Delay(500).ConfigureAwait(false);
			}
		}

		return (0, string.Empty);
	}
}
