using Purview.Containers.Images;

namespace Purview.Containers.Wsl;

public class ImageTests
{
	[Test]
	public async Task Parse_ShortName_DefaultsToDockerHubLibrary()
	{
		var reference = Image.Parse("alpine");

		await Assert.That(reference.Registry).IsEqualTo("docker.io");
		await Assert.That(reference.Repository).IsEqualTo("library/alpine");
		await Assert.That(reference.Tag).IsEqualTo("latest");
		await Assert.That(reference.FullReference).IsEqualTo("docker.io/library/alpine:latest");
	}

	[Test]
	public async Task Parse_ExplicitTag_IsPreserved()
	{
		var reference = Image.Parse("redis:7");

		await Assert.That(reference.Registry).IsEqualTo("docker.io");
		await Assert.That(reference.Repository).IsEqualTo("library/redis");
		await Assert.That(reference.Tag).IsEqualTo("7");
	}

	[Test]
	public async Task Parse_FullyQualified_IsNormalized()
	{
		var reference = Image.Parse("docker.io/library/alpine:latest");

		await Assert.That(reference.Registry).IsEqualTo("docker.io");
		await Assert.That(reference.Repository).IsEqualTo("library/alpine");
		await Assert.That(reference.Tag).IsEqualTo("latest");
	}

	[Test]
	public async Task Parse_PrivateRegistry_IsDetected()
	{
		var reference = Image.Parse("ghcr.io/acme/app:v1");

		await Assert.That(reference.Registry).IsEqualTo("ghcr.io");
		await Assert.That(reference.Repository).IsEqualTo("acme/app");
		await Assert.That(reference.Tag).IsEqualTo("v1");
	}

	[Test]
	public async Task MatchesStoredName_ShortVsQualified_Matches()
	{
		var reference = Image.Parse("docker.io/library/alpine:latest");

		await Assert.That(reference.MatchesStoredName("alpine:latest")).IsTrue();
		await Assert.That(reference.MatchesStoredName("redis:latest")).IsFalse();
		await Assert.That(reference.MatchesStoredName("alpine:edge")).IsFalse();
	}

	[Test]
	public async Task WithTag_ReturnsNewImage()
	{
		var reference = Image.Parse("redis").WithTag("7.4");

		await Assert.That(reference.Tag).IsEqualTo("7.4");
		await Assert.That(reference.FullReference).IsEqualTo("docker.io/library/redis:7.4");
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("redis:-bad")]
	[Arguments("redis:.leading-dot")]
	[Arguments("redis:tag with spaces")]
	[Arguments("bad image name")]
	public async Task Parse_Invalid_Throws(string value)
	{
		await Assert.That(() => Image.Parse(value)).Throws<ArgumentException>();
	}
}
