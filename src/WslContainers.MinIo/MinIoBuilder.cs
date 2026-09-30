namespace Purview.WslContainers.MinIo;

/// <summary>Fluent builder for a MinIO (S3-compatible object storage) test container.</summary>
public class MinIoBuilder : ContainerBuilder<MinIoBuilder, MinIoContainer, MinIoConfiguration>
{
	/// <summary>Default S3 API port.</summary>
	public const ushort ApiPort = 9000;

	/// <summary>Default console port.</summary>
	public const ushort ConsolePort = 9001;

	/// <summary>Default image (MinIO's official registry is Quay.io).</summary>
	public const string MinIoImage = "quay.io/minio/minio:latest";

	private string accessKey = "minioadmin";
	private Secret secretKey = Secret.From("minioadmin");

	/// <summary>Creates a builder with the default image.</summary>
	public MinIoBuilder()
		: this(MinIoImage) { }

	/// <summary>Creates a builder with a custom image.</summary>
	public MinIoBuilder(string image)
	{
		WithImage(image)
			.WithCommand("minio", "server", "/data", "--console-address", ":9001")
			.WithPortBinding(ApiPort, assignRandomHostPort: true)
			.WithPortBinding(ConsolePort, assignRandomHostPort: true);
	}

	/// <summary>Creates a builder using an explicit runtime.</summary>
	public MinIoBuilder(IContainerRuntime runtime)
		: base(runtime)
	{
		WithImage(MinIoImage)
			.WithCommand("minio", "server", "/data", "--console-address", ":9001")
			.WithPortBinding(ApiPort, assignRandomHostPort: true)
			.WithPortBinding(ConsolePort, assignRandomHostPort: true);
	}

	/// <summary>Sets the root access key.</summary>
	public MinIoBuilder WithAccessKey(string accessKey)
	{
		this.accessKey = accessKey;
		WithEnvironment("MINIO_ROOT_USER", accessKey);
		return this;
	}

	/// <summary>Sets the root secret key.</summary>
	public MinIoBuilder WithSecretKey(string secretKey)
	{
		this.secretKey = Secret.From(secretKey);
		WithEnvironment("MINIO_ROOT_PASSWORD", secretKey);
		return this;
	}

	/// <summary>Builds the immutable configuration (internal; used by the module's own tests).</summary>
	internal MinIoConfiguration BuildConfigurationForTesting() => BuildConfiguration();

	/// <inheritdoc />
	protected override MinIoConfiguration BuildConfiguration()
	{
		MinIoConfiguration configuration = base.BuildConfiguration();
		Dictionary<string, string> environment = new Dictionary<string, string>(
			configuration.Environment,
			StringComparer.Ordinal
		)
		{
			["MINIO_ROOT_USER"] = accessKey,
			["MINIO_ROOT_PASSWORD"] = secretKey.Value,
		};
		IReadOnlyList<IWaitStrategy> waitStrategies =
			configuration.WaitStrategies.Count > 0
				? configuration.WaitStrategies
				: new[] { (IWaitStrategy)Wait.ForLogMessage("Console:") };
		return configuration with
		{
			AccessKey = accessKey,
			SecretKey = secretKey,
			Environment = environment,
			WaitStrategies = waitStrategies,
		};
	}

	/// <inheritdoc />
	protected override void Validate(ContainerConfiguration configuration)
	{
		base.Validate(configuration);
		if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrEmpty(secretKey.Value))
		{
			throw new WslContainerConfigurationException("MinIO access key and secret key cannot be empty.");
		}
	}

	/// <inheritdoc />
	protected override MinIoContainer CreateContainer(MinIoConfiguration configuration)
	{
		return new MinIoContainer(configuration, Runtime ?? WslContainerRuntime.Instance);
	}
}
