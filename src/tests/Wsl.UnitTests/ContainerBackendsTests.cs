using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

/// <summary>
/// Covers the backend registry, the selection policy and the resolution rules the builders rely on. The
/// registry and the selection are process-wide static state, so the class opts out of parallel execution
/// and every test starts from a clean registry.
/// </summary>
[NotInParallel]
public class ContainerBackendsTests
{
	[Test]
	public async Task WslBackend_ReportsItsName()
	{
		await Assert.That(WslContainerBackend.Create().Name).IsEqualTo("wsl");
	}

	[Test]
	public async Task WslBackend_CreatesAWslContainerForADerivedConfiguration()
	{
		var container = new WslContainerBackend().CreateContainer(
			new DerivedConfiguration { Image = "alpine:3.19", Extra = "x" }
		);

		await Assert.That(container).IsTypeOf<WslContainer>();
		await Assert.That(container.Image).IsEqualTo("alpine:3.19");
	}

	[Test]
	public async Task GenericBuilder_Build_DefersTheBackendResolution()
	{
		using RegistryScope scope = new();

		// No backend is registered, yet Build() succeeds: the backend is resolved when the container starts.
		ContainerBuilder builder = new();
		var container = builder.WithImage("alpine").Build();

		await Assert.That(container).IsTypeOf<Container>();
		await Assert.That(container.Name).StartsWith("alpine-");
		await Assert.That(container.State).IsEqualTo(ContainerState.Created);
	}

	[Test]
	public async Task ResolveAsync_WithoutARegisteredBackend_ThrowsAnActionableError()
	{
		using RegistryScope scope = new();

		var exception = await ThrowsAsync<ContainerBackendUnavailableException>(() => ContainerBackends.ResolveAsync());

		await Assert.That(exception.Message).Contains("Purview.Containers.Wsl");
		await Assert.That(exception.Message).Contains("Purview.Containers.Docker");
	}

	[Test]
	public async Task ResolveAsync_ReturnsTheFirstUsableBackend()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one"));
		ContainerBackends.Register(new FakeBackend("two"));

		var resolved = await ContainerBackends.ResolveAsync();

		await Assert.That(resolved.Name).IsEqualTo("one");
	}

	[Test]
	public async Task ResolveAsync_SkipsAnUnusableBackend()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one", isAvailable: false));
		ContainerBackends.Register(new FakeBackend("two"));

		var resolved = await ContainerBackends.ResolveAsync();

		await Assert.That(resolved.Name).IsEqualTo("two");
	}

	[Test]
	public async Task ResolveAsync_WithoutAUsableBackend_ReportsEveryBackend()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one", isAvailable: false));
		ContainerBackends.Register(new ThrowingBackend("two"));

		var exception = await ThrowsAsync<ContainerBackendUnavailableException>(() => ContainerBackends.ResolveAsync());

		await Assert.That(exception.Message).Contains("one: unavailable");
		await Assert.That(exception.Message).Contains("two: unavailable");
		await Assert.That(exception.Message).Contains(ContainerBackends.SelectionEnvironmentVariable);
	}

	[Test]
	public async Task ResolveAsync_CachesTheSuccessfulResolution()
	{
		using RegistryScope scope = new();
		FakeBackend backend = new("one");
		ContainerBackends.Register(backend);

		await ContainerBackends.ResolveAsync();
		await ContainerBackends.ResolveAsync();

		await Assert.That(backend.Probes).IsEqualTo(1);
	}

	[Test]
	public async Task ResolveAsync_WithANamedBackend_PrefersItOverTheRegisteredOrder()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one"));
		ContainerBackends.Register(new FakeBackend("two"));
		ContainerBackends.Use(ContainerBackendSelection.Named("two"));

		var resolved = await ContainerBackends.ResolveAsync();

		await Assert.That(resolved.Name).IsEqualTo("two");
	}

	[Test]
	public async Task ResolveAsync_WithTheEnvironmentVariable_SelectsThatBackend()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one"));
		ContainerBackends.Register(new FakeBackend("two"));
		using EnvironmentScope environment = new(ContainerBackends.SelectionEnvironmentVariable, "two");

		var resolved = await ContainerBackends.ResolveAsync();

		await Assert.That(resolved.Name).IsEqualTo("two");
	}

	[Test]
	public async Task ResolveAsync_WithANamedBackendThatIsNotRegistered_ListsTheRegisteredOnes()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one"));
		ContainerBackends.Use(ContainerBackendSelection.Named("docker"));

		var exception = await ThrowsAsync<ContainerBackendUnavailableException>(() => ContainerBackends.ResolveAsync());

		await Assert.That(exception.Message).Contains("'docker' is not registered");
		await Assert.That(exception.Message).Contains("one");
	}

	[Test]
	public async Task ResolveAsync_WithANamedUnusableBackend_DoesNotFallBack()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one"));
		ContainerBackends.Register(new FakeBackend("two", isAvailable: false));
		ContainerBackends.Use(ContainerBackendSelection.Named("two"));

		var exception = await ThrowsAsync<ContainerBackendUnavailableException>(() => ContainerBackends.ResolveAsync());

		await Assert.That(exception.Message).Contains("'two' is not usable");
	}

	[Test]
	public async Task Use_PinsABackendInstanceOverEverythingElse()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("registered"));
		ContainerBackends.Use(new FakeBackend("pinned"));

		var resolved = await ContainerBackends.ResolveAsync();

		await Assert.That(resolved.Name).IsEqualTo("pinned");
	}

	[Test]
	public async Task Use_WithAPinnedUnusableBackend_ReportsIt()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("registered"));
		ContainerBackends.Use(new FakeBackend("pinned", isCompatible: false));

		var exception = await ThrowsAsync<ContainerBackendUnavailableException>(() => ContainerBackends.ResolveAsync());

		await Assert.That(exception.Message).Contains("The pinned container backend 'pinned' is not usable");
	}

	[Test]
	public async Task ProbeAllAsync_ReportsEveryRegisteredBackend()
	{
		using RegistryScope scope = new();
		ContainerBackends.Register(new FakeBackend("one"));
		ContainerBackends.Register(new FakeBackend("two", isAvailable: false));

		var probes = await ContainerBackends.ProbeAllAsync();

		await Assert.That(probes.Count).IsEqualTo(2);
		await Assert.That(probes[0].IsUsable).IsTrue();
		await Assert.That(probes[1].IsUsable).IsFalse();
	}

	static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
		where TException : Exception
	{
		var exception = await Assert.ThrowsAsync<TException>(action);
		ArgumentNullException.ThrowIfNull(exception);
		return exception;
	}

	sealed record DerivedConfiguration : ContainerConfiguration
	{
		public string Extra { get; init; } = string.Empty;
	}

	sealed class FakeBackend(string name, bool isAvailable = true, bool isCompatible = true) : IContainerBackend
	{
		public string Name { get; } = name;

		public int Probes { get; private set; }

		public IContainer CreateContainer(IContainerConfiguration configuration) => throw new NotSupportedException();

		public Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default)
		{
			Probes++;
			return Task.FromResult(
				new ContainerBackendInfo(
					Name,
					isAvailable,
					isCompatible,
					"1.0",
					isAvailable && isCompatible ? [] : ["missing"]
				)
			);
		}
	}

	sealed class ThrowingBackend(string name) : IContainerBackend
	{
		public string Name { get; } = name;

		public IContainer CreateContainer(IContainerConfiguration configuration) => throw new NotSupportedException();

		public Task<ContainerBackendInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("probe failed");
	}

	/// <summary>Clears the backend registry for the duration of a test and resets it on dispose.</summary>
	sealed class RegistryScope : IDisposable
	{
		public RegistryScope() => ContainerBackends.Reset();

		public void Dispose() => ContainerBackends.Reset();
	}

	/// <summary>Sets an environment variable for the duration of a test and restores it on dispose.</summary>
	sealed class EnvironmentScope : IDisposable
	{
		readonly string _name;
		readonly string? _previous;

		public EnvironmentScope(string name, string? value)
		{
			_name = name;
			_previous = Environment.GetEnvironmentVariable(name);
			Environment.SetEnvironmentVariable(name, value);
		}

		public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
	}
}
