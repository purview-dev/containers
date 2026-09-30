namespace Purview.WslContainers;

class WslInspectParserTests
{
	const string SampleInspect = /*lang=json,strict*/ """
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
		var ports = WslInspectParser.ParseHostPorts(SampleInspect);

		await Assert.That(ports.Count).IsEqualTo(1);
		await Assert.That(ports[8080]).IsEqualTo((ushort)61332);
	}

	[Test]
	public async Task ParseHostPorts_EmptyJson_ReturnsEmpty()
	{
		var ports = WslInspectParser.ParseHostPorts("{}");

		await Assert.That(ports.Count).IsEqualTo(0);
	}

	[Test]
	public async Task TryGetNetworkIp_ReadsBridgeIp()
	{
		var ip = WslInspectParser.TryGetNetworkIp(SampleInspect);

		await Assert.That(ip).IsEqualTo("172.17.0.3");
	}

	[Test]
	public async Task TryGetNetworkIp_MissingNetwork_ReturnsNull()
	{
		var ip = WslInspectParser.TryGetNetworkIp("{}");

		await Assert.That(ip).IsNull();
	}
}
