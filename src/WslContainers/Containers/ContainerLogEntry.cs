namespace Purview.WslContainers;

/// <summary>A single line of container init-process output.</summary>
public sealed record ContainerLogEntry(DateTimeOffset Timestamp, LogOutput Stream, string Text);
