using Purview.WslContainers;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.UnitTests;

public class WslInspectParserTests
{
	private const string SampleInspect = """
		{
		  "HostConfig": { "NetworkMode": "bridge" },
		  "NetworkSettings": {
		    "Networks": {
		      "bridge": { "IPAddress": "172.17.0.3", "Gateway": "172.17.0.1" }
		    }
		  },
		  "Ports": {
		    "8080/tcp": [ { "HostIp": "127.0.0.1", "HostPort": "61332" } ]
		  }
		}
		""";

	[Test]
	public async Task ParseHostPorts_ReadsMappedPorts()
	{
		IReadOnlyDictionary<ushort, ushort> ports = WslInspectParser.ParseHostPorts(SampleInspect);

		await Assert.That(ports.Count).IsEqualTo(1);
		await Assert.That(ports[8080]).IsEqualTo((ushort)61332);
	}

	[Test]
	public async Task ParseHostPorts_EmptyJson_ReturnsEmpty()
	{
		IReadOnlyDictionary<ushort, ushort> ports = WslInspectParser.ParseHostPorts("{}");

		await Assert.That(ports.Count).IsEqualTo(0);
	}

	[Test]
	public async Task TryGetNetworkIp_ReadsBridgeIp()
	{
		string? ip = WslInspectParser.TryGetNetworkIp(SampleInspect);

		await Assert.That(ip).IsEqualTo("172.17.0.3");
	}

	[Test]
	public async Task TryGetNetworkIp_MissingNetwork_ReturnsNull()
	{
		string? ip = WslInspectParser.TryGetNetworkIp("{}");

		await Assert.That(ip).IsNull();
	}
}
