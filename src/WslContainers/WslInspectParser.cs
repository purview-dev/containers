using System.Text.Json;

namespace Purview.WslContainers;

/// <summary>Parses the Docker-compatible JSON returned by <c>Container.Inspect()</c>.</summary>
internal static class WslInspectParser
{
	public static IReadOnlyDictionary<ushort, ushort> ParseHostPorts(string inspectJson)
	{
		Dictionary<ushort, ushort> result = new();
		try
		{
			using JsonDocument document = JsonDocument.Parse(inspectJson);
			if (
				!document.RootElement.TryGetProperty("Ports", out JsonElement ports)
				|| ports.ValueKind != JsonValueKind.Object
			)
			{
				return result;
			}

			foreach (JsonProperty property in ports.EnumerateObject())
			{
				// Key format: "8080/tcp"
				string key = property.Name;
				int slash = key.IndexOf('/', StringComparison.Ordinal);
				if (
					slash <= 0
					|| !ushort.TryParse(key.AsSpan(0, slash), out ushort containerPort)
					|| property.Value.ValueKind != JsonValueKind.Array
				)
				{
					continue;
				}

				foreach (JsonElement binding in property.Value.EnumerateArray())
				{
					if (
						binding.TryGetProperty("HostPort", out JsonElement hostPort)
						&& hostPort.ValueKind == JsonValueKind.String
						&& ushort.TryParse(hostPort.GetString(), out ushort host)
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
			using JsonDocument document = JsonDocument.Parse(inspectJson);
			if (
				document.RootElement.TryGetProperty("NetworkSettings", out JsonElement networkSettings)
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
}
