namespace Purview.Containers.Environment;

public sealed class ContainerPathMappingTests
{
	static ContainerPathMapping CreateMapping(string root, string daemonRoot = "/shared") => new(root, daemonRoot);

	[Test]
	public async Task Map_NestedPath_PreservesRelativeLayout()
	{
		// Arrange
		var root = Path.Combine(Path.GetTempPath(), "project");
		var mapping = CreateMapping(root);

		// Act
		var result = mapping.Map(Path.Combine(root, "folder with spaces", "file.json"));

		// Assert
		await Assert.That(result).IsEqualTo("/shared/folder with spaces/file.json");
	}

	[Test]
	public async Task Map_SimilarlyNamedSibling_DoesNotMatch()
	{
		// Arrange
		var root = Path.Combine(Path.GetTempPath(), "project");
		var mapping = CreateMapping(root);

		// Act
		var result = mapping.Map(root + "-other");

		// Assert
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task Map_DaemonFilesystemRoot_PreservesAbsolutePath()
	{
		// Arrange
		var root = Path.GetTempPath();
		var mapping = CreateMapping(root, "/");

		// Act
		var result = mapping.Map(Path.Combine(root, "child"));

		// Assert
		await Assert.That(result).IsEqualTo("/child");
	}
}
