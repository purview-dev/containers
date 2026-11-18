using System.Reflection;
using System.Runtime.CompilerServices;

namespace Purview.Containers.Wsl;

/// <summary>
/// Guards the build shape of the net11.0 assembly documented in
/// <c>docs/wiki/Consumer-Requirements.md</c>. Purview.BuildSdk 2.2.0 makes both .NET 11 features
/// opt-in, so <c>src/Directory.Build.props</c> enables them explicitly; these tests fail if that
/// opt-in is lost.
/// </summary>
public class BuildFeatureTests
{
	/// <summary>
	/// <c>PurviewRuntimeAsync=true</c> compiles with <c>runtime-async=on</c>, which removes the
	/// compiler-generated state machines: async methods carry no
	/// <see cref="AsyncStateMachineAttribute" />, the runtime tracks suspension instead, and live stack
	/// traces name the real methods.
	/// </summary>
	[Test]
	public async Task Net11AssemblyUsesRuntimeAsync()
	{
		var stateMachine = typeof(WslContainer)
			.GetMethod(nameof(WslContainer.StartAsync))!
			.GetCustomAttribute<AsyncStateMachineAttribute>();

		await Assert.That(stateMachine).IsNull();
	}

	/// <summary>
	/// <c>MemorySafetyRules=2</c> compiles with the C# 15 <c>updated-memory-safety-rules</c> feature,
	/// which marks the module with <c>MemorySafetyRulesAttribute</c>.
	/// </summary>
	[Test]
	public async Task Net11AssemblyUsesUpdatedMemorySafetyRules()
	{
		var assembly = typeof(WslContainer).Assembly;
		var marked = assembly
			.ManifestModule.GetCustomAttributesData()
			.Concat(assembly.GetCustomAttributesData())
			.Any(a => a.AttributeType.Name == "MemorySafetyRulesAttribute");

		await Assert.That(marked).IsTrue();
	}
}
