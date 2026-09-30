namespace Purview.WslContainers;

/// <summary>
/// Wraps a sensitive value so it is never printed by default diagnostics.
/// <c>ToString()</c> and <c>Value</c> exposure require explicit intent.
/// </summary>
public readonly record struct Secret
{
	private readonly string _value;

	private Secret(string value)
	{
		_value = value;
	}

	/// <summary>Creates a secret from a plain value.</summary>
	public static Secret From(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return new Secret(value);
	}

	/// <summary>The underlying value. Handle with care.</summary>
	public string Value => _value;

	/// <inheritdoc />
	public override string ToString() => "<redacted>";
}
