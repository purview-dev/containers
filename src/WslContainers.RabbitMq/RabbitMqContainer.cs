namespace Purview.WslContainers.RabbitMq;

/// <summary>A throwaway RabbitMQ broker running on WSL Containers.</summary>
public sealed class RabbitMqContainer : WslContainer
{
	private readonly RabbitMqConfiguration configuration;

	internal RabbitMqContainer(RabbitMqConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
	{
		this.configuration = configuration;
	}

	/// <summary>
	/// AMQP connection string (<c>amqp://user:pass@localhost:{port}/{vhost}</c>). Safe to call after
	/// <see cref="IContainer.StartAsync" />.
	/// </summary>
	public string GetConnectionString()
	{
		return GetAmqpEndpoint().ToString();
	}

	/// <summary>AMQP endpoint (RabbitMQ.Client <c>ConnectionFactory.Uri</c>).</summary>
	public Uri GetAmqpEndpoint()
	{
		UriBuilder builder = new UriBuilder
		{
			Scheme = "amqp",
			Host = "localhost",
			Port = GetMappedPublicPort(RabbitMqBuilder.AmqpPort),
			UserName = configuration.Username,
			Password = configuration.Password.Value,
		};
		builder.Path = configuration.VirtualHost is "/" or "" ? "/" : "/" + configuration.VirtualHost.TrimStart('/');
		return builder.Uri;
	}

	/// <summary>Management web endpoint.</summary>
	public Uri GetManagementEndpoint()
	{
		return new UriBuilder("http", "localhost", GetMappedPublicPort(RabbitMqBuilder.ManagementPort))
		{
			Path = "/",
		}.Uri;
	}
}
