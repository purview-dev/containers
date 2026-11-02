namespace Purview.Containers.Docker;

public sealed class WslDockerHostTests
{
	static readonly Uri LocalEndpoint = new("tcp://127.0.0.1:2376");

	static WslDockerHost CreateHost(Func<string[], string?> run) => new("DockerEngine", run);

	[Test]
	public async Task FindDistribution_MultipleEngines_SelectsTheMatchingDaemon()
	{
		// Arrange
		List<string[]> calls = [];
		string? Run(string[] arguments)
		{
			calls.Add(arguments);
			return arguments[0] == "--list" ? "Ubuntu\r\nDockerEngine\r\n"
				: arguments[1] == "Ubuntu" ? "other-daemon"
				: "connected-daemon";
		}

		// Act
		var result = WslDockerHost.FindDistribution(
			LocalEndpoint,
			"6.6-microsoft-standard-WSL2",
			"Ubuntu",
			"connected-daemon",
			Run
		);

		// Assert
		await Assert.That(result).IsEqualTo("DockerEngine");
		await Assert.That(calls).Count().IsEqualTo(3);
		await Assert.That(calls[2]).Contains("unix:///var/run/docker.sock");
		await Assert.That(calls[2]).Contains("DOCKER_TLS_VERIFY");
	}

	[Test]
	[Arguments("tcp://remote.example:2376", "6.6-microsoft-standard-WSL2", "Ubuntu")]
	[Arguments("npipe://./pipe/docker_engine", "6.6-microsoft-standard-WSL2", "Docker Desktop")]
	[Arguments("tcp://127.0.0.1:2376", "6.6-microsoft-standard-WSL2", "Docker Desktop")]
	[Arguments("tcp://127.0.0.1:2376", "6.8.0-generic", "Ubuntu")]
	public async Task FindDistribution_NotALocalWslEngine_DoesNotProbeWsl(
		string endpoint,
		string kernel,
		string operatingSystem
	)
	{
		// Arrange
		var probed = false;
		string? Run(string[] _)
		{
			probed = true;
			return null;
		}

		// Act
		var result = WslDockerHost.FindDistribution(new Uri(endpoint), kernel, operatingSystem, "daemon", Run);

		// Assert
		await Assert.That(result).IsNull();
		await Assert.That(probed).IsFalse();
	}

	[Test]
	[Arguments(null)]
	[Arguments("Ubuntu\n")]
	public async Task FindDistribution_NoMatchingDaemon_LeavesPathsUnmapped(string? distributions)
	{
		// Arrange
		string? Run(string[] arguments) => arguments[0] == "--list" ? distributions : "other-daemon";

		// Act
		var result = WslDockerHost.FindDistribution(LocalEndpoint, "microsoft-WSL2", "Ubuntu", "connected-daemon", Run);

		// Assert
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task Resolve_WindowsPath_UsesTheOwningDistributionsActualMountRoot()
	{
		// Arrange
		string[]? actual = null;
		var host = CreateHost(arguments =>
		{
			actual = arguments;
			return "/windows/p/project with spaces";
		});

		// Act
		var result = host.Resolve(@"P:\project with spaces");

		// Assert
		await Assert.That(result).IsEqualTo("/windows/p/project with spaces");
		await Assert
			.That(actual)
			.IsEquivalentTo([
				"--distribution",
				"DockerEngine",
				"--exec",
				"wslpath",
				"-a",
				"-u",
				"P:/project with spaces",
			]);
	}

	[Test]
	[Arguments("/srv/project")]
	[Arguments(@"\\server\share\project")]
	[Arguments("named-volume")]
	public async Task Resolve_NonDriveSource_PreservesTheSource(string source)
	{
		// Arrange
		var host = CreateHost(_ => throw new InvalidOperationException("WSL must not be invoked."));

		// Act
		var result = host.Resolve(source);

		// Assert
		await Assert.That(result).IsEqualTo(source);
	}

	[Test]
	[Arguments(null)]
	[Arguments("P:/project")]
	public async Task Resolve_TranslationFails_ReportsTheFailure(string? translated)
	{
		// Arrange
		var host = CreateHost(_ => translated);

		// Act
		var act = () => host.Resolve(@"P:\project");

		// Assert
		await Assert.That(act).Throws<InvalidOperationException>();
	}
}
