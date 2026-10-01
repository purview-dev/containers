namespace Purview.Containers.Runtime;

#pragma warning disable CA1032 // Implement standard exception constructors

/// <summary>Base exception for all errors raised by a container backend.</summary>
public class ContainerException : Exception
{
	public ContainerException(string message)
		: base(message) { }

	public ContainerException(string message, Exception innerException)
		: base(message, innerException) { }
}

/// <summary>Invalid container configuration detected before any backend operation.</summary>
public class ContainerConfigurationException : ContainerException
{
	public ContainerConfigurationException(string message)
		: base(message) { }

	public ContainerConfigurationException(string message, Exception innerException)
		: base(message, innerException) { }
}

/// <summary>The backend's prerequisites are missing or incompatible with this library.</summary>
public class ContainerPrerequisiteException(string message) : ContainerException(message) { }

/// <summary>A requested feature is not supported by the selected backend.</summary>
public class ContainerNotSupportedException(string message) : ContainerException(message) { }

/// <summary>Starting the container failed (image, runtime, or environment).</summary>
public class ContainerStartupException(string message, Exception innerException)
	: ContainerException(message, innerException) { }

/// <summary>An operation did not complete within its configured timeout.</summary>
public class ContainerTimeoutException(string message) : ContainerException(message) { }

/// <summary>
/// No container backend is registered, or the explicitly selected backend cannot be used on this host.
/// </summary>
public class ContainerBackendUnavailableException(string message) : ContainerException(message) { }
