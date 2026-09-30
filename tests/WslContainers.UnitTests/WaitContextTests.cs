using Purview.WslContainers;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.UnitTests;

public class WaitContextTests
{
	private static WaitContext Create(IReadOnlyDictionary<ushort, ushort> mappings)
	{
		FakeContainer container = new FakeContainer { PortMappings = mappings };
		return new WaitContext(container, mappings, "172.17.0.9");
	}

	[Test]
	public async Task GetHostPort_ReturnsMappedPort()
	{
		WaitContext context = Create(new Dictionary<ushort, ushort> { [8080] = 54321 });

		await Assert.That(context.GetHostPort(8080)).IsEqualTo(54321);
		await Assert.That(context.GetHostPort(80)).IsNull();
	}

	[Test]
	public async Task FirstHostPort_ReturnsFirstMapping()
	{
		WaitContext context = Create(new Dictionary<ushort, ushort> { [8080] = 54321, [8081] = 54322 });

		await Assert.That(context.FirstHostPort).IsEqualTo(54321);
	}

	[Test]
	public async Task FirstHostPort_EmptyMappings_IsNull()
	{
		WaitContext context = Create(new Dictionary<ushort, ushort>());

		await Assert.That(context.FirstHostPort).IsNull();
		await Assert.That(context.NetworkIp).IsEqualTo("172.17.0.9");
	}
}
