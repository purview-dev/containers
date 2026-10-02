namespace Purview.Containers;

/// <summary>
/// Base class for the typed containers a service module exposes. It creates the backend container on
/// <see cref="StartAsync" /> and delegates every <see cref="IContainer" /> member to it, which keeps the
/// module packages backend-neutral: a module package depends on this abstraction assembly only.
/// </summary>
public abstract class ContainerBase : IContainer
{
	IContainer? _container;
	IConnectionStringProvider? _connectionStringProvider;
	Action? _configureConnectionStringProvider;
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
		Configuration = configuration;
		Backend = backend;
		Name = ContainerName.Generate(configuration);
	}

	/// <summary>
	/// The backend that will create the container, or <c>null</c> to resolve it on
	/// <see cref="StartAsync" />.
	/// </summary>
	protected IContainerBackend? Backend { get; }

	/// <summary>The immutable configuration this container was built from.</summary>
	protected IContainerConfiguration Configuration { get; }

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
	public string Image => Configuration.Image;

	/// <inheritdoc />
	public virtual async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		if (Interlocked.Exchange(ref _started, 1) == 1)
		{
			return;
		}

		var backend = Backend ?? await ContainerBackends.ResolveAsync(cancellationToken).ConfigureAwait(false);
		_container = backend.CreateContainer(WithResolvedName());
		await _container.StartAsync(cancellationToken).ConfigureAwait(false);
		_configureConnectionStringProvider?.Invoke();
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
	public virtual string GetConnectionString(ConnectionMode connectionMode = ConnectionMode.Host)
	{
		_ = Container; // throws when the container has not been started
		if (_connectionStringProvider is { } provider)
		{
			return provider.GetConnectionString(connectionMode);
		}

		// If the container has started but no connection string provider was configured, we can only support the default host connection string.
		return connectionMode switch
		{
			ConnectionMode.Host => GetDefaultHostConnectionString(),
			ConnectionMode.Container => throw new ConnectionStringModeNotSupportedException(connectionMode, GetType()),
			_ => throw new ArgumentOutOfRangeException(nameof(connectionMode), connectionMode, null),
		};
	}

	/// <inheritdoc />
	public virtual string GetConnectionString(string name, ConnectionMode connectionMode = ConnectionMode.Host)
	{
		_ = Container; // throws when the container has not been started
		if (_connectionStringProvider is { } provider)
		{
			return provider.GetConnectionString(name, connectionMode);
		}

		// If the container has started but no connection string provider was configured, we can only support the default host connection string.
		throw new ConnectionStringNameNotSupportedException(GetType(), name);
	}

	string GetDefaultHostConnectionString()
	{
		var first = GetMappedPublicPorts().FirstOrDefault();
		if (first.Key == 0 && first.Value == 0)
		{
			throw new ConnectionStringNotAvailableException(ConnectionMode.Host, GetType());
		}

		// The default host connection string is always
		return $"127.0.0.1:{first.Value}";
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
		return Configuration is ContainerConfiguration concrete ? concrete with { Name = Name } : Configuration;
	}

	/// <summary>Wires an explicit connection string provider, configured once the container has started.</summary>
	internal void SetConnectionStringProvider<TContainer, TConfiguration>(
		IConnectionStringProvider<TContainer, TConfiguration> provider,
		TContainer container,
		TConfiguration configuration
	)
		where TContainer : IContainer
		where TConfiguration : IContainerConfiguration
	{
		ArgumentNullException.ThrowIfNull(provider);
		_connectionStringProvider = provider;
		_configureConnectionStringProvider = () => provider.Configure(container, configuration);
	}
}
