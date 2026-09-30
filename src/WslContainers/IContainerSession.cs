using Purview.WslContainers.Images;

namespace Purview.WslContainers;

/// <summary>A WSLC session: the shared host for images and containers.</summary>
public interface IContainerSession : IAsyncDisposable
{
	/// <summary>Session name (machine-unique).</summary>
	string Name { get; }

	/// <summary>Ensures the image is present according to the pull policy.</summary>
	Task EnsureImageAsync(
		string image,
		PullPolicy policy,
		IProgress<ImagePullProgress>? progress = null,
		RegistryCredentials? credentials = null,
		CancellationToken cancellationToken = default
	);

	/// <summary>Lists images present in the session store.</summary>
	Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default);

	/// <summary>Terminates and releases the session.</summary>
	Task TerminateAsync(CancellationToken cancellationToken = default);
}
