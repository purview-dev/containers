namespace Purview.Containers.Wsl;

public class ConnectionStringProviderTests
{
	sealed class HostOnlyProvider : ContainerConnectionStringProvider<IContainer, IContainerConfiguration>
	{
		readonly string _host;

		public HostOnlyProvider(string host) => _host = host;

		/// <inheritdoc />
		protected override string GetHostConnectionString() => _host;
	}

	sealed class HostAndContainerProvider : ContainerConnectionStringProvider<IContainer, IContainerConfiguration>
	{
		readonly string _host;
		readonly string _container;

		public HostAndContainerProvider(string host, string container)
		{
			_host = host;
			_container = container;
		}

		/// <inheritdoc />
		protected override string GetHostConnectionString() => _host;

		/// <inheritdoc />
		protected override string GetContainerConnectionString() => _container;
	}

	[Test]
	public async Task GetConnectionString_DefaultsToHostMode()
	{
		await using FakeContainer container = new();
		HostOnlyProvider provider = new("host-connection");
		provider.Configure(container, new ContainerConfiguration());

		await Assert.That(provider.GetConnectionString()).IsEqualTo("host-connection");
		await Assert.That(provider.GetConnectionString(ConnectionMode.Host)).IsEqualTo("host-connection");
	}

	[Test]
	public async Task GetConnectionString_ContainerModeThrowsWhenNotOverridden()
	{
		await using FakeContainer container = new();
		HostOnlyProvider provider = new("host-connection");
		provider.Configure(container, new ContainerConfiguration());

		await Assert
			.That(() => provider.GetConnectionString(ConnectionMode.Container))
			.Throws<ConnectionStringModeNotSupportedException>();
	}

	[Test]
	public async Task GetConnectionString_ContainerModeIsSupportedWhenOverridden()
	{
		await using FakeContainer container = new();
		HostAndContainerProvider provider = new("host-connection", "container-connection");
		provider.Configure(container, new ContainerConfiguration());

		await Assert.That(provider.GetConnectionString(ConnectionMode.Container)).IsEqualTo("container-connection");
	}

	[Test]
	public async Task GetConnectionString_ThrowsWhenNotConfigured()
	{
		HostOnlyProvider provider = new("host-connection");

		await Assert
			.That(() => provider.GetConnectionString())
			.Throws<ConnectionStringProviderNotConfiguredException>();
	}

	[Test]
	public async Task GetConnectionString_ThrowsWhenHostStringIsEmpty()
	{
		await using FakeContainer container = new();
		HostOnlyProvider provider = new(string.Empty);
		provider.Configure(container, new ContainerConfiguration());

		await Assert.That(() => provider.GetConnectionString()).Throws<ConnectionStringNotAvailableException>();
	}

	[Test]
	public async Task GetConnectionString_NamedThrowsByDefault()
	{
		await using FakeContainer container = new();
		HostOnlyProvider provider = new("host-connection");
		provider.Configure(container, new ContainerConfiguration());

		await Assert
			.That(() => provider.GetConnectionString("primary"))
			.Throws<ConnectionStringNameNotSupportedException>();
	}

	[Test]
	public async Task FakeContainer_ReturnsHostConnectionStringFromFirstMapping()
	{
		await using FakeContainer container = new()
		{
			PortMappings = new Dictionary<ushort, ushort> { [6379] = 32768 },
		};

		await Assert.That(container.GetConnectionString()).IsEqualTo("127.0.0.1:32768");
	}
}
