namespace Purview.Containers.Environment;

/// <summary>Configuration for a single container runtime connection.</summary>
public sealed class ContainerEnvironmentOptions
{
	/// <summary>The Docker-compatible CLI. Its existing context, environment and TLS settings are preserved.</summary>
	public string RuntimeCommand { get; set; } = "docker";

	/// <summary>An optional running WSL distribution used to translate Windows paths.</summary>
	public string? WslDistribution { get; set; }

	/// <summary>Explicit client-root to daemon-root mappings, with the most specific root winning.</summary>
	public Dictionary<string, string> PathMappings { get; set; } = [];

	/// <summary>The Linux image used to verify file sharing. Pin or mirror it for restricted environments.</summary>
	public string ProbeImage { get; set; } = "alpine:3.19";

	/// <summary>The maximum duration of each runtime or WSL command, including an image pull.</summary>
	public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
