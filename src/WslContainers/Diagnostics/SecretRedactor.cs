namespace Purview.WslContainers;

/// <summary>Redacts sensitive values from diagnostics and configuration rendering.</summary>
internal static class SecretRedactor
{
	private static readonly string[] SensitiveKeyMarkers =
	{
		"PASSWORD",
		"PASS",
		"TOKEN",
		"SECRET",
		"PWD",
		"CREDENTIAL",
		"KEY",
	};

	public static string RedactEnvironment(IReadOnlyDictionary<string, string> environment)
	{
		if (environment.Count == 0)
		{
			return "{}";
		}

		IEnumerable<string> parts = environment.Select(pair =>
			IsSensitive(pair.Key) ? $"{pair.Key} = <redacted>" : $"{pair.Key} = {pair.Value}"
		);
		return "{ " + string.Join(", ", parts) + " }";
	}

	private static bool IsSensitive(string key)
	{
		foreach (string marker in SensitiveKeyMarkers)
		{
			if (key.Contains(marker, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
