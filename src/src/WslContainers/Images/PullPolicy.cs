namespace Purview.WslContainers.Images;

/// <summary>Controls when an image is pulled.</summary>
public enum PullPolicy
{
	/// <summary>Pull only when the image is not present in the session store.</summary>
	Missing = 0,

	/// <summary>Always attempt a pull.</summary>
	Always = 1,

	/// <summary>Never pull; fail if the image is not present.</summary>
	Never = 2,
}
