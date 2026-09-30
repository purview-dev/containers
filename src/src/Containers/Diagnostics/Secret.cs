namespace Purview.Containers.Diagnostics;

/// <summary>
/// Wraps a sensitive value so it is never printed by default diagnostics.
/// <c>ToString()</c> and <c>Value</c> exposure require explicit intent.
/// </summary>
public readonly record struct Secret
{
	Secret(string value)
	{
		Value = value;
	}

	/// <summary>Creates a secret from a plain value.</summary>
	public static Secret From(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return new Secret(value);
	}

	/// <summary>The underlying value. Handle with care.</summary>
	public string Value { get; }

	/// <inheritdoc />
	public override string ToString() => "<redacted>";
}
