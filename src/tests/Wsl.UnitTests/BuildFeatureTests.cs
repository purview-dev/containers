using System.Reflection;
using System.Runtime.CompilerServices;

namespace Purview.Containers.Wsl;

/// <summary>
/// Guards the build shape of the net11.0 assembly documented in
/// <c>docs/wiki/Consumer-Requirements.md</c>. The net11.0 build is compiled with
/// <c>runtime-async=on</c> (Purview.BuildSdk's <c>PurviewRuntimeAsync</c> default), which removes the
/// compiler-generated state machines: async methods carry no <see cref="AsyncStateMachineAttribute" />,
/// the runtime tracks suspension instead, and live stack traces name the real methods.
/// </summary>
public class BuildFeatureTests
{
	[Test]
	public async Task Net11AssemblyUsesRuntimeAsync()
	{
		var stateMachine = typeof(WslContainer)
			.GetMethod(nameof(WslContainer.StartAsync))!
			.GetCustomAttribute<AsyncStateMachineAttribute>();

		await Assert.That(stateMachine).IsNull();
	}
}
