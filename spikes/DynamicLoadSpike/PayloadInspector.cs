using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DynamicLoadSpike;

/// <summary>
/// Reads the WSLC projection's metadata without loading it, so the probe can report the projection's
/// real target framework and the exact dependency set (name + version) that a runtime payload has to
/// ship. Runs on any OS.
/// </summary>
internal static class PayloadInspector
{
	public static void Inspect(string path)
	{
		if (!File.Exists(path))
		{
			Console.WriteLine($"[static] payload assembly not found: {path}");
			return;
		}

		using var stream = File.OpenRead(path);
		using var pe = new PEReader(stream);
		if (!pe.HasMetadata)
		{
			Console.WriteLine("[static] payload has no CLI metadata (unexpected for a managed projection).");
			return;
		}

		var md = pe.GetMetadataReader();
		var definition = md.GetAssemblyDefinition();
		Console.WriteLine($"[static] assembly    : {md.GetString(definition.Name)} {definition.Version}");
		Console.WriteLine($"[static] file        : {path}");

		foreach (var handle in definition.GetCustomAttributes())
		{
			var attribute = md.GetCustomAttribute(handle);
			var name = AttributeTypeName(md, attribute);
			if (
				!name.EndsWith("TargetFrameworkAttribute", StringComparison.Ordinal)
				&& !name.EndsWith("TargetPlatformAttribute", StringComparison.Ordinal)
			)
			{
				continue;
			}

			var label = name[(name.LastIndexOf('.') + 1)..];
			Console.WriteLine($"[static] {label, -24}: {StringArgument(md, attribute) ?? "<none>"}");
		}

		Console.WriteLine("[static] references  :");
		foreach (var handle in md.AssemblyReferences)
		{
			var reference = md.GetAssemblyReference(handle);
			Console.WriteLine($"[static]   ref        : {md.GetString(reference.Name)} {reference.Version}");
		}
	}

	static string AttributeTypeName(MetadataReader md, CustomAttribute attribute) =>
		attribute.Constructor.Kind switch
		{
			HandleKind.MemberReference => TypeName(
				md,
				md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
			),
			HandleKind.MethodDefinition => TypeName(
				md,
				md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()
			),
			_ => "<unknown>",
		};

	static string TypeName(MetadataReader md, EntityHandle handle) =>
		handle.Kind switch
		{
			HandleKind.TypeReference => Define(md, md.GetTypeReference((TypeReferenceHandle)handle)),
			HandleKind.TypeDefinition => Define(md, md.GetTypeDefinition((TypeDefinitionHandle)handle)),
			_ => "<type>",
		};

	static string Define(MetadataReader md, TypeReference reference)
	{
		var ns = md.GetString(reference.Namespace);
		return string.IsNullOrEmpty(ns) ? md.GetString(reference.Name) : $"{ns}.{md.GetString(reference.Name)}";
	}

	static string Define(MetadataReader md, TypeDefinition definition)
	{
		var ns = md.GetString(definition.Namespace);
		return string.IsNullOrEmpty(ns) ? md.GetString(definition.Name) : $"{ns}.{md.GetString(definition.Name)}";
	}

	// The first fixed argument of TargetFrameworkAttribute/TargetPlatformAttribute is a string.
	static string? StringArgument(MetadataReader md, CustomAttribute attribute)
	{
		try
		{
			var reader = md.GetBlobReader(attribute.Value);
			reader.ReadUInt16(); // attribute prolog 0x0001
			return reader.ReadSerializedString();
		}
		catch
		{
			return null;
		}
	}
}
