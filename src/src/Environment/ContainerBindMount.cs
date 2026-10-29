using System.Text;

namespace Purview.Containers.Environment;

/// <summary>A Docker CLI bind mount, with CSV-safe formatting for paths containing commas or quotes.</summary>
public sealed record ContainerBindMount(string Source, string Target, bool IsReadOnly = false)
{
	/// <summary>Formats the value of a Docker <c>--mount</c> option, not a shell command.</summary>
	public override string ToString() =>
		string.Join(
			",",
			new[] { "type=bind", $"source={Source}", $"target={Target}" }
				.Concat(IsReadOnly ? ["readonly"] : Array.Empty<string>())
				.Select(Quote)
		);

	internal static string Quote(string value) =>
		value.IndexOfAny([',', '"', '\r', '\n']) >= 0
			? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
			: value;

	internal static IReadOnlyList<string> ParseFields(string value)
	{
		List<string> fields = [];
		StringBuilder field = new();
		var quoted = false;

		for (var i = 0; i < value.Length; i++)
		{
			var character = value[i];
			if (character == '"')
			{
				if (quoted && i + 1 < value.Length && value[i + 1] == '"')
				{
					field.Append('"');
					i++;
				}
				else
				{
					quoted = !quoted;
				}
			}
			else if (character == ',' && !quoted)
			{
				fields.Add(field.ToString());
				field.Clear();
			}
			else
			{
				field.Append(character);
			}
		}

		if (quoted)
			throw new FormatException("The container mount has an unterminated CSV field.");

		fields.Add(field.ToString());
		return fields;
	}
}
