using Purview.WslContainers;

namespace WslContainers.UnitTests;

internal sealed class FakeContainer : IContainer
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
		if (!PortMappings.TryGetValue(containerPort, out ushort hostPort))
		{
			throw new InvalidOperationException($"No mapping for {containerPort}.");
		}

		return hostPort;
	}

	public IReadOnlyDictionary<ushort, ushort> GetMappedPublicPorts() => PortMappings;

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
