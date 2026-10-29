namespace Purview.Containers;

/// <summary>A host file or directory copied to an absolute container path before startup.</summary>
public sealed record ContainerResourceMapping(string Source, string Target);
