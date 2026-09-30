using Purview.WslContainers;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.UnitTests;

public class SecretTests
{
	[Test]
	public async Task ToString_RedactsValue()
	{
		Secret secret = Secret.From("super-secret-password");

		await Assert.That(secret.ToString()).IsEqualTo("<redacted>");
		await Assert.That(secret.ToString()).DoesNotContain("super");
	}

	[Test]
	public async Task Value_ReturnsUnderlying()
	{
		Secret secret = Secret.From("super-secret-password");

		await Assert.That(secret.Value).IsEqualTo("super-secret-password");
	}

	[Test]
	public async Task From_Null_Throws()
	{
		await Assert.That(() => Secret.From(null!)).Throws<ArgumentNullException>();
	}
}
