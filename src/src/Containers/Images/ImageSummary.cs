namespace Purview.Containers.Images;

/// <summary>Summary of an image present in the session store.</summary>
public sealed record ImageSummary(string Name, ulong Size, DateTimeOffset CreatedTimestamp);
