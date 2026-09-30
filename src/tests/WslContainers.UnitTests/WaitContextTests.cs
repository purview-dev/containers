using Purview.WslContainers.Waiting;

namespace Purview.WslContainers;

class WaitContextTests
{
	static (WaitContext, IAsyncDisposable) Create(IReadOnlyDictionary<ushort, ushort> mappings)
	{
		FakeContainer container = new()
		{ PortMappings = mappings };
		return (new WaitContext(container, mappings, "172.17.0.9"), container);
	}

	[Test]
	public async Task GetHostPort_ReturnsMappedPort()
	{
		var (context, container) = Create(new Dictionary<ushort, ushort> { [8080] = 54321 });

		await using (container)
		{
			await Assert.That(context.GetHostPort(8080)).IsEqualTo(54321);
			await Assert.That(context.GetHostPort(80)).IsNull();
		}
	}

	[Test]
	public async Task FirstHostPort_ReturnsFirstMapping()
	{
		var (context, container) = Create(new Dictionary<ushort, ushort> { [8080] = 54321, [8081] = 54322 });

		await using (container)
			await Assert.That(context.FirstHostPort).IsEqualTo(54321);
	}

	[Test]
	public async Task FirstHostPort_EmptyMappings_IsNull()
	{
		var (context, container) = Create(new Dictionary<ushort, ushort>());

		await using (container)
		{
			await Assert.That(context.FirstHostPort).IsNull();
			await Assert.That(context.NetworkIp).IsEqualTo("172.17.0.9");
		}
	}
}
