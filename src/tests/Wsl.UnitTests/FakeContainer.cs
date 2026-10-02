namespace Purview.Containers.Wsl;

public sealed class FakeContainer : IContainer
{
	public string Id { get; init; } = "fake-id";

	public string Name { get; init; } = "fake-container";

	public ContainerState State { get; set; }

	public string Image { get; init; } = "fake:latest";

	public string Logs { get; set; } = string.Empty;

	public IReadOnlyDictionary<ushort, ushort> PortMappings { get; init; } = new Dictionary<ushort, ushort>();

	public Func<string[], ExecResult>? Exec { get; set; }

	public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<ExecResult> ExecAsync(
		string[] command,
		ExecOptions? options = null,
		CancellationToken cancellationToken = default
	)
	{
		return Task.FromResult(Exec?.Invoke(command) ?? new ExecResult(0, string.Empty, string.Empty));
	}

	public ushort GetMappedPublicPort(ushort containerPort)
	{
		if (!PortMappings.TryGetValue(containerPort, out var hostPort))
		{
			throw new InvalidOperationException($"No mapping for {containerPort}.");
		}

		// Return the mapped host port
		return hostPort;
	}

	public IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts() => PortMappings;

	public string GetConnectionString(ConnectionMode connectionMode = ConnectionMode.Host)
	{
		if (connectionMode != ConnectionMode.Host)
		{
			throw new ConnectionStringModeNotSupportedException(connectionMode, GetType());
		}

		var first = PortMappings.FirstOrDefault();
		if (first.Key == 0 && first.Value == 0)
		{
			throw new ConnectionStringNotAvailableException(connectionMode, GetType());
		}

		// Return the connection string in the format "
		return $"127.0.0.1:{first.Value}";
	}

	public string GetConnectionString(string name, ConnectionMode connectionMode = ConnectionMode.Host)
	{
		throw new ConnectionStringNameNotSupportedException(GetType(), name);
	}

	public Task<string> GetLogsAsync(LogOutput? stream = null, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(stream is LogOutput.Stderr ? string.Empty : Logs);
	}

	public async IAsyncEnumerable<ContainerLogEntry> GetLogsAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		await Task.Yield();
		yield break;
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
