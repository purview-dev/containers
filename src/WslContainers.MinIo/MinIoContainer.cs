namespace Purview.WslContainers.MinIo;

/// <summary>A throwaway MinIO (S3-compatible object storage) instance running on WSL Containers.</summary>
public sealed class MinIoContainer : WslContainer
{
	private readonly MinIoConfiguration configuration;

	internal MinIoContainer(MinIoConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
	{
		this.configuration = configuration;
	}

	/// <summary>S3 API endpoint. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public Uri GetApiEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(MinIoBuilder.ApiPort)}");
	}

	/// <summary>Web console endpoint.</summary>
	public Uri GetConsoleEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(MinIoBuilder.ConsolePort)}");
	}

	/// <summary>Root access key.</summary>
	public string GetAccessKey() => configuration.AccessKey;

	/// <summary>Root secret key. Handle as a secret.</summary>
	public Secret GetSecretKey() => configuration.SecretKey;
}
