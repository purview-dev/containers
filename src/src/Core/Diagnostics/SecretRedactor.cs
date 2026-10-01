namespace Purview.Containers.Diagnostics;

/// <summary>Redacts sensitive values from diagnostics and configuration rendering.</summary>
static class SecretRedactor
{
	static readonly string[] SensitiveKeyMarkers = ["PASSWORD", "PASS", "TOKEN", "SECRET", "PWD", "CREDENTIAL", "KEY"];

	public static string RedactEnvironment(IReadOnlyDictionary<string, string> environment)
	{
		if (environment.Count == 0)
		{
			return "{}";
		}

		var parts = environment.Select(pair =>
			IsSensitive(pair.Key) ? $"{pair.Key} = <redacted>" : $"{pair.Key} = {pair.Value}"
		);
		return "{ " + string.Join(", ", parts) + " }";
	}

	static bool IsSensitive(string key)
	{
		foreach (var marker in SensitiveKeyMarkers)
		{
			if (key.Contains(marker, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
