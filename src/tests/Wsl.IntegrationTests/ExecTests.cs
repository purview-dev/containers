namespace Purview.Containers.Wsl;

[Explicit]
public class ExecTests
{
	[Test]
	public async Task Exec_ReturnsStdoutAndExitCode()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sleep", "300")
			.Build();
		await container.StartAsync();

		var result = await container.ExecAsync(["/bin/echo", "hello-exec"]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stdout.Trim()).IsEqualTo("hello-exec");
		await Assert.That(result.Stderr).IsEmpty();
	}

	[Test]
	public async Task Exec_PropagatesEnvironmentAndWorkingDirectory()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sleep", "300")
			.Build();
		await container.StartAsync();

		var result = await container.ExecAsync(
			["/bin/sh", "-c", "echo $FOO; pwd"],
			new ExecOptions
			{
				Environment = new Dictionary<string, string> { ["FOO"] = "bar" },
				WorkingDirectory = "/tmp",
			}
		);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stdout.Trim()).IsEqualTo("bar\n/tmp");
	}

	[Test]
	public async Task Exec_ReturnsNonZeroExitCodeAndStderr()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sleep", "300")
			.Build();
		await container.StartAsync();

		var result = await container.ExecAsync(["/bin/sh", "-c", "echo boom >&2; exit 9"]);

		await Assert.That(result.ExitCode).IsEqualTo(9);
		await Assert.That(result.Stderr.Trim()).IsEqualTo("boom");
	}

	[Test]
	public async Task Exec_TimesOut_AndKillsProcess()
	{
		await WslcTest.SkipIfUnavailableAsync();

		await using var container = new ContainerBuilder()
			.WithImage("alpine:latest")
			.WithCommand("/bin/sleep", "300")
			.Build();
		await container.StartAsync();

		var result = await container.ExecAsync(
			["/bin/sleep", "60"],
			new ExecOptions { Timeout = TimeSpan.FromSeconds(2) }
		);

		await Assert.That(result.ExitCode).IsNotEqualTo(0);
	}
}
