namespace Purview.WslContainers.Nats;

/// <summary>A throwaway NATS broker running on WSL Containers.</summary>
public sealed class NatsContainer : WslContainer
{
	internal NatsContainer(NatsConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime) { }

	/// <summary>Client endpoint (<c>nats://127.0.0.1:{port}</c>). Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public Uri GetClientEndpoint()
	{
		return new Uri($"nats://127.0.0.1:{GetMappedPublicPort(NatsBuilder.ClientPort)}");
	}

	/// <summary>Monitoring HTTP endpoint.</summary>
	public Uri GetMonitoringEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(NatsBuilder.MonitoringPort)}");
	}

	/// <summary>NATS client connection string (the <c>nats://</c> URL).</summary>
	public string GetConnectionString()
	{
		return GetClientEndpoint().ToString();
	}
}
