namespace Purview.WslContainers.MinIo;

/// <summary>Immutable configuration for a MinIO test container.</summary>
public sealed record MinIoConfiguration : ContainerConfiguration
{
	/// <summary>Root access key (MINIO_ROOT_USER).</summary>
	public string AccessKey { get; init; } = "minioadmin";

	/// <summary>Root secret key (MINIO_ROOT_PASSWORD). Rendered as redacted.</summary>
	public Secret SecretKey { get; init; } = Secret.From("minioadmin");
}
