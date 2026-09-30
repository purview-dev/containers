using Purview.WslContainers.Diagnostics;

namespace Purview.WslContainers.MinIO;

/// <summary>A throwaway MinIO (S3-compatible object storage) instance running on WSL Containers.</summary>
public sealed class MinIOContainer : WslContainer
{
	readonly MinIOConfiguration _configuration;

	internal MinIOContainer(MinIOConfiguration configuration, IContainerRuntime runtime)
		: base(configuration, runtime)
	{
		_configuration = configuration;
	}

	/// <summary>S3 API endpoint. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public Uri GetAPIEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(MinIOBuilder.APIPort)}");
	}

	/// <summary>Web console endpoint.</summary>
	public Uri GetConsoleEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(MinIOBuilder.ConsolePort)}");
	}

	/// <summary>Root access key.</summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate")]
	public string GetAccessKey() => _configuration.AccessKey;

	/// <summary>Root secret key. Handle as a secret.</summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate")]
	public Secret GetSecretKey() => _configuration.SecretKey;
}
