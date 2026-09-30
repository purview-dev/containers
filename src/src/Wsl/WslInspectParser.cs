using System.Text.Json;

namespace Purview.Containers.Wsl;

/// <summary>Parses the Docker-compatible JSON returned by <c>Container.Inspect()</c>.</summary>
static class WslInspectParser
{
	public static IReadOnlyDictionary<ushort, ushort> ParseHostPorts(string inspectJson)
	{
		Dictionary<ushort, ushort> result = [];
		try
		{
			using var document = JsonDocument.Parse(inspectJson);
			if (!document.RootElement.TryGetProperty("Ports", out var ports) || ports.ValueKind != JsonValueKind.Object)
			{
				return result;
			}

			foreach (var property in ports.EnumerateObject())
			{
				// Key format: "8080/tcp"
				var key = property.Name;
				var slash = key.IndexOf('/', StringComparison.Ordinal);
				if (
					slash <= 0
					|| !ushort.TryParse(key.AsSpan(0, slash), out var containerPort)
					|| property.Value.ValueKind != JsonValueKind.Array
				)
				{
					continue;
				}

				foreach (var binding in property.Value.EnumerateArray())
				{
					if (
						binding.TryGetProperty("HostPort", out var hostPort)
						&& hostPort.ValueKind == JsonValueKind.String
						&& ushort.TryParse(hostPort.GetString(), out var host)
					)
					{
						result[containerPort] = host;
						break;
					}
				}
			}
		}
		catch (JsonException) { }

		return result;
	}

	public static string? TryGetNetworkIp(string inspectJson, string network = "bridge")
	{
		try
		{
			using var document = JsonDocument.Parse(inspectJson);
			if (
				document.RootElement.TryGetProperty("NetworkSettings", out var networkSettings)
				&& networkSettings.TryGetProperty("Networks", out var networks)
				&& networks.TryGetProperty(network, out var net)
				&& net.TryGetProperty("IPAddress", out var ip)
				&& ip.ValueKind == JsonValueKind.String
			)
			{
				var value = ip.GetString() ?? string.Empty;
				return string.IsNullOrEmpty(value) ? null : value;
			}

			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
