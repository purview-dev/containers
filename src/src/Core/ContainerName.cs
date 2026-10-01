namespace Purview.Containers;

/// <summary>
/// Generates the unique container name used when a configuration does not set one. Shared by every
/// backend so a name is stable before and after the container starts.
/// </summary>
public static class ContainerName
{
	/// <summary>Builds a name from the image's short name, the process id and a random suffix.</summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "A container name must always be produced, even for an unparseable image reference."
	)]
	public static string Generate(string image)
	{
		string shortName;
		try
		{
			shortName = Images.Image.Parse(image).ShortName;
		}
		catch
		{
			shortName = "container";
		}

		string safe = new([
			.. shortName.Select(character =>
				char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-'
			),
		]);
		return $"{safe}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..6]}";
	}

	/// <summary>Builds a name for a configuration, preferring an explicitly configured name.</summary>
	public static string Generate(IContainerConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		return configuration.Name ?? Generate(configuration.Image);
	}
}
