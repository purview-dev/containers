namespace Purview.Containers.Images;

/// <summary>Progress reported while pulling an image.</summary>
public sealed record ImagePullProgress(string? Id, string Status, ulong CurrentBytes, ulong TotalBytes);
