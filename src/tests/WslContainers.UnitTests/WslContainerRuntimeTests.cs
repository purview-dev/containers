using System.Runtime.InteropServices;

namespace Purview.WslContainers;

public class WslContainerRuntimeTests
{
	const int ErrorSharingViolation = unchecked((int)0x80070020);

	[Test]
	public async Task SharingViolation_OnDefaultSharedStore_FallsBackToIsolatedStore()
	{
		COMException exception = new(
			"The process cannot access the file because it is being used by another process.",
			ErrorSharingViolation
		);

		var shouldFallBack = WslContainerRuntime.ShouldFallBackToIsolatedStore(
			exception,
			isUsingDefaultSharedStore: true
		);

		await Assert.That(shouldFallBack).IsTrue();
	}

	[Test]
	public async Task SharingViolation_OnExplicitStore_DoesNotFallBack()
	{
		COMException exception = new(
			"The process cannot access the file because it is being used by another process.",
			ErrorSharingViolation
		);

		var shouldFallBack = WslContainerRuntime.ShouldFallBackToIsolatedStore(
			exception,
			isUsingDefaultSharedStore: false
		);

		await Assert.That(shouldFallBack).IsFalse();
	}

	[Test]
	public async Task NonSharingViolation_DoesNotFallBack()
	{
		COMException exception = new("Unexpected failure", unchecked((int)0x8000FFFF));

		var shouldFallBack = WslContainerRuntime.ShouldFallBackToIsolatedStore(
			exception,
			isUsingDefaultSharedStore: true
		);

		await Assert.That(shouldFallBack).IsFalse();
	}
}
