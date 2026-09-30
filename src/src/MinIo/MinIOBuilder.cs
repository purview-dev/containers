using Purview.WslContainers.Diagnostics;
using Purview.WslContainers.Runtime;
using Purview.WslContainers.Waiting;

namespace Purview.WslContainers.MinIO;

/// <summary>Fluent builder for a MinIO (S3-compatible object storage) test container.</summary>
public class MinIOBuilder : ContainerBuilder<MinIOBuilder, MinIOContainer, MinIOConfiguration>
{
	/// <summary>Default S3 API port.</summary>
	public const ushort APIPort = 9000;

	/// <summary>Default console port.</summary>
	public const ushort ConsolePort = 9001;

	/// <summary>Default image (MinIO's official registry is Quay.io).</summary>
	public const string MinIOImage = "quay.io/minio/minio:latest";

	string _accessKey = "minioadmin";

	Secret _secretKey = Secret.From("minioadmin");

	/// <summary>Creates a builder with the default image.</summary>
	public MinIOBuilder()
		: this(MinIOImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public MinIOBuilder(string image)
	{
		WithImage(image)
			.WithCommand("minio", "server", "/data", "--console-address", ":9001")
			.WithPortBinding(APIPort, assignRandomHostPort: true)
			.WithPortBinding(ConsolePort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public MinIOBuilder(IContainerRuntime runtime)
		: base(runtime)
	{
		WithImage(MinIOImage)
			.WithCommand("minio", "server", "/data", "--console-address", ":9001")
			.WithPortBinding(APIPort, assignRandomHostPort: true)
			.WithPortBinding(ConsolePort, assignRandomHostPort: true);
	}

	/// <summary>Sets the root access key.</summary>
	public MinIOBuilder WithAccessKey(string accessKey)
	{
		_accessKey = accessKey;
		WithEnvironment("MINIO_ROOT_USER", accessKey);
		return this;
	}

	/// <summary>Sets the root secret key.</summary>
	public MinIOBuilder WithSecretKey(string secretKey)
	{
		_secretKey = Secret.From(secretKey);
		WithEnvironment("MINIO_ROOT_PASSWORD", secretKey);
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal MinIOConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override MinIOConfiguration BuildConfiguration()
	{
		var configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new(
			configuration.Environment,
			StringComparer.Ordinal
		)
		{
			["MINIO_ROOT_USER"] = _accessKey,
			["MINIO_ROOT_PASSWORD"] = _secretKey.Value,
		};
		var waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForLogMessage("Console:") };
		return configuration with
		{
			AccessKey = _accessKey,
			SecretKey = _secretKey,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (string.IsNullOrWhiteSpace(_accessKey) || string.IsNullOrEmpty(_secretKey.Value))
		{
			throw new WslContainerConfigurationException("MinIO access key and secret key cannot be empty.");
		}
	}

	/// <inheritdoc />
	protected override MinIOContainer CreateContainer(MinIOConfiguration configuration)
	{
		return new MinIOContainer(configuration, Runtime ?? WslContainerRuntime.Instance);
	}
}
