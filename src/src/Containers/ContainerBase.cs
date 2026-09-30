namespace Purview.Containers;

/// <summary>
/// Base class for the typed containers a service module exposes. It creates the backend container on
/// <see cref="StartAsync" /> and delegates every <see cref="IContainer" /> member to it, which keeps the
/// module packages backend-neutral: a module package depends on this abstraction assembly only.
/// </summary>
public abstract class ContainerBase : IContainer
{
	readonly IContainerConfiguration _configuration;
	readonly IContainerBackend? _backend;
	IContainer? _container;
	int _started;
	int _disposed;

	/// <summary>
	/// Creates a container bound to a backend. Prefer building via a module builder. When
	/// <paramref name="backend" /> is <c>null</c> (the default) the backend is resolved on
	/// <see cref="StartAsync" /> through <see cref="ContainerBackends.ResolveAsync" />.
	/// </summary>
	protected ContainerBase(IContainerConfiguration configuration, IContainerBackend? backend = null)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		_configuration = configuration;
		_backend = backend;
		Name = ContainerName.Generate(configuration);
	}

	/// <summary>
	/// The backend that will create the container, or <c>null</c> to resolve it on
	/// <see cref="StartAsync" />.
	/// </summary>
	protected IContainerBackend? Backend => _backend;

	/// <summary>The immutable configuration this container was built from.</summary>
	protected IContainerConfiguration Configuration => _configuration;

	/// <summary>The started backend container.</summary>
	/// <exception cref="InvalidOperationException">The container has not been started.</exception>
	protected IContainer Container =>
		_container ?? throw new InvalidOperationException("Container has not been started. Call StartAsync() first.");

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public string Id => _container?.Id ?? string.Empty;

	/// <inheritdoc />
	public ContainerState State => _container?.State ?? ContainerState.Created;

	/// <inheritdoc />
	public string Image => _configuration.Image;

	/// <inheritdoc />
	public virtual async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		if (Interlocked.Exchange(ref _started, 1) == 1)
		{
			return;
		}

		var backend = _backend ?? await ContainerBackends.ResolveAsync(cancellationToken).ConfigureAwait(false);
		_container = backend.CreateContainer(WithResolvedName());
		await _container.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public virtual Task StopAsync(CancellationToken cancellationToken = default)
	{
		return _container?.StopAsync(cancellationToken) ?? Task.CompletedTask;
	}

	/// <inheritdoc />
	public virtual Task<ExecResult> ExecAsync(
		string[] command,
		ExecOptions? options = null,
		CancellationToken cancellationToken = default
	)
	{
		return Container.ExecAsync(command, options, cancellationToken);
	}

	/// <inheritdoc />
	public virtual ushort GetMappedPublicPort(ushort containerPort)
	{
		return Container.GetMappedPublicPort(containerPort);
	}

	/// <inheritdoc />
	public virtual IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts()
	{
		return Container.GetMappedPublicPorts();
	}

	/// <inheritdoc />
	public virtual Task<string> GetLogsAsync(LogOutput? stream = null, CancellationToken cancellationToken = default)
	{
		return Container.GetLogsAsync(stream, cancellationToken);
	}

	/// <inheritdoc />
	public virtual IAsyncEnumerable<ContainerLogEntry> GetLogsAsync(CancellationToken cancellationToken)
	{
		return Container.GetLogsAsync(cancellationToken);
	}

	/// <inheritdoc />
	public virtual async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		if (_container is not null)
		{
			await _container.DisposeAsync().ConfigureAwait(false);
		}

		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Snapshots the configuration with the resolved container name so the backend creates the container
	/// under the name this instance reports.
	/// </summary>
	IContainerConfiguration WithResolvedName()
	{
		return _configuration is ContainerConfiguration concrete ? concrete with { Name = Name } : _configuration;
	}
}
