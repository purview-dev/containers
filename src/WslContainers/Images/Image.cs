namespace Purview.WslContainers.Images;

/// <summary>
/// A parsed container image reference (registry / repository / tag). Immutable; invalid references
/// fail fast at parse time so bad images are rejected during configuration rather than at pull time.
/// </summary>
public readonly record struct Image
{
	private const string DefaultRegistry = "docker.io";
	private const string DefaultTag = "latest";

	private Image(string registry, string repository, string tag)
	{
		Registry = registry;
		Repository = repository;
		Tag = tag;
	}

	/// <summary>Registry host, e.g. <c>docker.io</c>.</summary>
	public string Registry { get; }

	/// <summary>Repository path without the registry host, e.g. <c>library/alpine</c>.</summary>
	public string Repository { get; }

	/// <summary>Image tag, defaulting to <c>latest</c> when omitted.</summary>
	public string Tag { get; }

	/// <summary>Full reference for pull, e.g. <c>docker.io/library/alpine:latest</c>.</summary>
	public string FullReference => $"{Registry}/{Repository}:{Tag}";

	/// <summary>Last repository segment, e.g. <c>alpine</c>.</summary>
	public string ShortName => Repository[(Repository.LastIndexOf('/', StringComparison.Ordinal) + 1)..];

	/// <summary>
	/// Parses an image reference. Accepts <c>alpine</c>, <c>alpine:latest</c>, <c>docker.io/library/alpine:latest</c>,
	/// <c>ghcr.io/org/app:v1</c>. Throws <see cref="ArgumentException" /> for invalid references.
	/// </summary>
	public static Image Parse(string reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		reference = reference.Trim();
		if (reference.Length == 0)
		{
			throw new ArgumentException("Image reference cannot be empty.", nameof(reference));
		}

		string tag = DefaultTag;
		int lastSlash = reference.LastIndexOf('/', StringComparison.Ordinal);
		int lastColon = reference.LastIndexOf(':', StringComparison.Ordinal);
		if (lastColon > lastSlash)
		{
			tag = NormalizeTag(reference[(lastColon + 1)..]);
			reference = reference[..lastColon];
		}

		string registry;
		string repository;
		int firstSlash = reference.IndexOf('/', StringComparison.Ordinal);
		if (firstSlash < 0)
		{
			registry = DefaultRegistry;
			repository = reference;
		}
		else
		{
			string firstSegment = reference[..firstSlash];
			if (
				firstSegment.Contains('.', StringComparison.Ordinal)
				|| firstSegment.Contains(':', StringComparison.Ordinal)
				|| firstSegment.Equals("localhost", StringComparison.OrdinalIgnoreCase)
			)
			{
				registry = firstSegment;
				repository = reference[(firstSlash + 1)..];
			}
			else
			{
				registry = DefaultRegistry;
				repository = reference;
			}
		}

		registry = registry.ToLowerInvariant();
		repository = repository.ToLowerInvariant();
		if (registry == DefaultRegistry && repository.IndexOf('/', StringComparison.Ordinal) < 0)
		{
			repository = $"library/{repository}";
		}

		ValidateRepository(reference, repository);
		return new Image(registry, repository, tag);
	}

	/// <summary>Returns a copy of this image with a different tag.</summary>
	public Image WithTag(string tag)
	{
		return new Image(Registry, Repository, NormalizeTag(tag));
	}

	/// <summary>
	/// Returns true when a name returned by <see cref="Microsoft.WSL.Containers.Session.GetImages" />
	/// (e.g. <c>alpine:latest</c>) refers to this image.
	/// </summary>
	public bool MatchesStoredName(string storedName)
	{
		if (string.IsNullOrEmpty(storedName))
		{
			return false;
		}

		try
		{
			Image other = Parse(storedName);
			return string.Equals(other.Tag, Tag, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(other.ShortName, ShortName, StringComparison.OrdinalIgnoreCase);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	/// <inheritdoc />
	public override string ToString() => FullReference;

	private static string NormalizeTag(string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
		{
			throw new ArgumentException("Image tag cannot be empty.", nameof(tag));
		}

		if (tag.Length > 128)
		{
			throw new ArgumentException($"Image tag '{tag}' exceeds 128 characters.", nameof(tag));
		}

		if (tag[0] is '.' or '-' || !tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'))
		{
			throw new ArgumentException($"Image tag '{tag}' is invalid.", nameof(tag));
		}

		return tag;
	}

	private static void ValidateRepository(string reference, string repository)
	{
		if (repository.Length == 0)
		{
			throw new ArgumentException($"Image reference '{reference}' has an empty repository.", nameof(reference));
		}

		if (repository.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '.' or '-')))
		{
			throw new ArgumentException(
				$"Image repository '{repository}' contains invalid characters.",
				nameof(reference)
			);
		}
	}
}
