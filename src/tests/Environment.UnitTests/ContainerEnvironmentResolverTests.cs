using System.Diagnostics;

namespace Purview.Containers.Environment;

public sealed class ContainerEnvironmentResolverTests
{
	static string TestDirectory => Path.GetTempPath();

	static ContainerEnvironmentResolver CreateEnvironment(
		Func<string, string, CancellationToken, Task<bool>> probe,
		Dictionary<string, string>? mappings = null,
		Func<string, CancellationToken, Task<IReadOnlyList<string>>>? candidates = null
	) =>
		new(
			new ContainerEnvironmentOptions { PathMappings = mappings ?? [] },
			probe,
			candidates ?? ((_, _) => Task.FromResult<IReadOnlyList<string>>([]))
		);

	[Test]
	public async Task ResolveBindSourceAsync_NativeSharingWorks_DoesNotTryWsl(CancellationToken cancellationToken)
	{
		// Arrange
		using var environment = CreateEnvironment(
			(_, _, _) => Task.FromResult(true),
			candidates: (_, _) => throw new InvalidOperationException("WSL must not be queried")
		);

		// Act
		var result = await environment.ResolveBindSourceAsync(TestDirectory, cancellationToken);

		// Assert
		await Assert.That(result).IsEqualTo(Path.GetFullPath(TestDirectory));
	}

	[Test]
	public async Task ResolveBindSourceAsync_NativeSharingFails_VerifiesCandidatesAndCachesSuccess(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var probes = 0;
		using var environment = CreateEnvironment(
			(_, daemon, _) =>
			{
				probes++;
				return Task.FromResult(daemon == "/shared/temp");
			},
			candidates: (_, _) => Task.FromResult<IReadOnlyList<string>>(["/wrong/temp", "/shared/temp"])
		);

		// Act
		var first = await environment.ResolveBindSourceAsync(TestDirectory, cancellationToken);
		var second = await environment.ResolveBindSourceAsync(TestDirectory, cancellationToken);

		// Assert
		await Assert.That(first).IsEqualTo("/shared/temp");
		await Assert.That(second).IsEqualTo(first);
		await Assert.That(probes).IsEqualTo(3);
	}

	[Test]
	public async Task ResolveBindSourceAsync_ExplicitMappingFails_DoesNotSilentlyFallBack(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var environment = CreateEnvironment(
			(_, _, _) => Task.FromResult(false),
			new() { [TestDirectory] = "/configured" },
			(_, _) => throw new InvalidOperationException("Explicit mapping must be authoritative")
		);

		// Act
		Func<Task> act = async () => await environment.ResolveBindSourceAsync(TestDirectory, cancellationToken);

		// Assert
		await Assert
			.That(act)
			.Throws<InvalidOperationException>()
			.WithMessageContaining("CONTAINER_ENVIRONMENT_PATH_MAPPINGS", StringComparison.Ordinal);
	}

	[Test]
	public async Task ResolveBindSourceAsync_CancelledProbe_DoesNotCacheFailure(CancellationToken cancellationToken)
	{
		// Arrange
		var probes = 0;
		using var environment = CreateEnvironment(
			(_, _, _) => ++probes == 1 ? throw new OperationCanceledException() : Task.FromResult(true)
		);
		Func<Task> first = async () => await environment.ResolveBindSourceAsync(TestDirectory, cancellationToken);

		// Act
		await Assert.That(first).Throws<OperationCanceledException>();
		var result = await environment.ResolveBindSourceAsync(TestDirectory, cancellationToken);

		// Assert
		await Assert.That(result).IsEqualTo(Path.GetFullPath(TestDirectory));
		await Assert.That(probes).IsEqualTo(2);
	}

	[Test]
	public async Task ResolveBindMountsAsync_QuotedPathAndVolume_OnlyRewritesBindSource(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var directory = Path.Combine(TestDirectory, "container-env," + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			using var environment = CreateEnvironment(
				(_, _, _) => Task.FromResult(true),
				new() { [directory] = "/daemon/path,with comma" }
			);
			ProcessStartInfo startInfo = new("docker");
			foreach (
				var argument in new[]
				{
					"run",
					"--mount",
					new ContainerBindMount(directory, "/data", true).ToString(),
					"--mount=type=volume,source=cache,target=/cache",
					"image",
				}
			)
				startInfo.ArgumentList.Add(argument);

			// Act
			await environment.ResolveBindMountsAsync(startInfo, cancellationToken);

			// Assert
			await Assert
				.That(startInfo.ArgumentList[2])
				.IsEqualTo("type=bind,\"source=/daemon/path,with comma\",target=/data,readonly");
			await Assert.That(startInfo.ArgumentList[3]).IsEqualTo("--mount=type=volume,source=cache,target=/cache");
		}
		finally
		{
			Directory.Delete(directory);
		}
	}

	[Test]
	public async Task ResolveBindMountsAsync_ImageCommandHasMountOption_DoesNotRewriteIt(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var environment = CreateEnvironment(
			(_, _, _) => throw new InvalidOperationException("No runtime bind mounts")
		);
		ProcessStartInfo startInfo = new("docker");
		foreach (
			var argument in new[]
			{
				"run",
				"--rm",
				"--workdir",
				"/work",
				"--ipc=host",
				"image",
				"--mount",
				"type=bind,source=not-a-client-path,target=/data",
			}
		)
			startInfo.ArgumentList.Add(argument);

		// Act
		await environment.ResolveBindMountsAsync(startInfo, cancellationToken);

		// Assert
		await Assert.That(startInfo.ArgumentList[7]).IsEqualTo("type=bind,source=not-a-client-path,target=/data");
	}

	[Test]
	public async Task ResolveBindSourceAsync_OverlappingMappings_UsesMostSpecificRoot(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var parent = Path.GetPathRoot(TestDirectory)!;
		using var environment = CreateEnvironment(
			(_, _, _) => Task.FromResult(true),
			new() { [parent] = "/broad", [TestDirectory] = "/specific" }
		);

		// Act
		var result = await environment.ResolveBindSourceAsync(TestDirectory, cancellationToken);

		// Assert
		await Assert.That(result).IsEqualTo("/specific");
	}

	[Test]
	public async Task ResolveBindSourceAsync_File_MapsItsParentWithoutChangingTheFileName(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var path = Path.Combine(TestDirectory, "container-env-" + Guid.NewGuid().ToString("N") + ".json");
		await File.WriteAllTextAsync(path, "{}", cancellationToken);
		try
		{
			using var environment = CreateEnvironment(
				(_, _, _) => Task.FromResult(true),
				new() { [TestDirectory] = "/shared" }
			);

			// Act
			var result = await environment.ResolveBindSourceAsync(path, cancellationToken);

			// Assert
			await Assert.That(result).IsEqualTo("/shared/" + Path.GetFileName(path));
		}
		finally
		{
			File.Delete(path);
		}
	}
}
