using System.Diagnostics.CodeAnalysis;

namespace Purview.Containers;

/// <summary>
/// Base class for the typed containers a service module exposes. It creates the backend container on
/// <see cref="StartAsync" /> and delegates every <see cref="IContainer" /> member to it, which keeps the
/// module packages backend-neutral: a module package depends on this abstraction assembly only.
/// </summary>
[SuppressMessage(
	"Usage",
	"CA2213:Disposable fields should be disposed",
	Justification = "The gate has no native wait handle and remains usable by concurrent callers unwinding after disposal."
)]
public abstract class ContainerBase : IContainer
{
	IContainer? _container;
	IConnectionStringProvider? _connectionStringProvider;
	Action? _configureConnectionStringProvider;
	int _started;
	int _disposed;
	int _cleanupStarted;
	readonly CancellationTokenSource _startupCancellation = new();
	readonly SemaphoreSlim _lifecycleGate = new(1, 1);

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
	public string? NetworkIp => _container?.NetworkIp;

	/// <inheritdoc />
	[SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "Failed-start cleanup must preserve the original startup/cancellation exception."
	)]
	public virtual async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			_startupCancellation.Token
		);
		deadline.CancelAfter(Configuration.StartupTimeout);
		await _lifecycleGate.WaitAsync(deadline.Token).ConfigureAwait(false);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
			deadline.Token.ThrowIfCancellationRequested();
			if (Volatile.Read(ref _started) == 1)
				return;
			var backend = Backend ?? await ContainerBackends.ResolveAsync(deadline.Token).ConfigureAwait(false);
			_container = backend.CreateContainer(ConfigureForStart(WithResolvedName()));
			await _container.StartAsync(deadline.Token).ConfigureAwait(false);
			_configureConnectionStringProvider?.Invoke();
			Volatile.Write(ref _started, 1);
		}
		catch
		{
			if (_container is not null)
			{
				try
				{
					await _container
						.DisposeAsync()
						.AsTask()
						.WaitAsync(Configuration.CleanupTimeout, CancellationToken.None)
						.ConfigureAwait(false);
				}
				catch { }
			}
			Volatile.Write(ref _disposed, 1);
			throw;
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	/// <inheritdoc />
	public virtual async Task StopAsync(CancellationToken cancellationToken = default)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Configuration.StopTimeout);
		await _lifecycleGate.WaitAsync(deadline.Token).ConfigureAwait(false);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
			deadline.Token.ThrowIfCancellationRequested();
			if (_container is not null)
				await _container.StopAsync(deadline.Token).ConfigureAwait(false);
		}
		finally
		{
			_lifecycleGate.Release();
		}
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
	public virtual Task CopyAsync(string source, string target, CancellationToken cancellationToken = default) =>
		Container.CopyAsync(source, target, cancellationToken);

	/// <inheritdoc />
	public virtual Task DeleteAsync(CancellationToken cancellationToken = default) =>
		Container.DeleteAsync(cancellationToken);

	/// <inheritdoc />
	public virtual Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
		Container.ReadFileAsync(path, cancellationToken);

	/// <inheritdoc />
	public virtual Task<long> WaitForExitAsync(CancellationToken cancellationToken = default) =>
		Container.WaitForExitAsync(cancellationToken);

	/// <inheritdoc />
	public virtual async ValueTask DisposeAsync()
	{
		Volatile.Write(ref _disposed, 1);
		if (Interlocked.Exchange(ref _cleanupStarted, 1) == 1)
		{
			return;
		}

		using CancellationTokenSource deadline = new(Configuration.CleanupTimeout);
		await _startupCancellation.CancelAsync().ConfigureAwait(false);
		var entered = false;
		try
		{
			await _lifecycleGate.WaitAsync(deadline.Token).ConfigureAwait(false);
			entered = true;
			if (_container is not null)
				await _container.DisposeAsync().AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
			GC.SuppressFinalize(this);
		}
		finally
		{
			if (entered)
				_lifecycleGate.Release();
			_startupCancellation.Dispose();
		}
	}

	/// <summary>
	/// Adjusts the configuration the backend container is created from, immediately before it is created. Use
	/// it for values only known at start time (for example the address of a dependency the module started
	/// first). The default returns <paramref name="configuration" /> unchanged.
	/// </summary>
	protected virtual IContainerConfiguration ConfigureForStart(IContainerConfiguration configuration) => configuration;

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
