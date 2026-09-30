namespace Purview.Containers;

/// <summary>
/// Chooses which container backend runs new containers: automatic detection, or one named backend.
/// </summary>
/// <remarks>
/// The value comes from, in order of precedence: <see cref="ContainerBackends.Use(ContainerBackendSelection?)" />,
/// the <c>PURVIEW_CONTAINERS_BACKEND</c> environment variable (see
/// <see cref="ContainerBackends.SelectionEnvironmentVariable" />), and finally <see cref="Auto" />.
/// A backend named here is used even when another backend would also work, and the resolution fails with
/// the named backend's diagnostics instead of silently falling back.
/// </remarks>
public readonly record struct ContainerBackendSelection
{
	ContainerBackendSelection(string? name) => Name = name;

	/// <summary>Probe every registered backend and use the first usable one.</summary>
	public static ContainerBackendSelection Auto { get; } = new(null);

	/// <summary>Uses the backend registered under <paramref name="name" /> (e.g. <c>wsl</c>, <c>docker</c>).</summary>
	public static ContainerBackendSelection Named(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		return new ContainerBackendSelection(name.Trim());
	}

	/// <summary>The backend name, or <c>null</c> for automatic detection.</summary>
	public string? Name { get; }

	/// <summary>True when the backend should be detected by probing.</summary>
	public bool IsAuto => Name is null;

	/// <summary>
	/// Parses a configuration value. <c>null</c>, empty, whitespace and <c>auto</c> (case-insensitive)
	/// mean automatic detection; any other value names a backend.
	/// </summary>
	public static ContainerBackendSelection Parse(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
		{
			return Auto;
		}

		return Named(value);
	}

	/// <summary>Reads the selection from the <c>PURVIEW_CONTAINERS_BACKEND</c> environment variable.</summary>
	public static ContainerBackendSelection FromEnvironment() =>
		Parse(Environment.GetEnvironmentVariable(ContainerBackends.SelectionEnvironmentVariable));

	/// <inheritdoc />
	public override string ToString() => Name ?? "auto";
}
