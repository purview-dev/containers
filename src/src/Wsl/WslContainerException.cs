using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

#pragma warning disable CA1032 // Implement standard exception constructors

/// <summary>Base exception for errors raised by the WSL Containers (WSLC) backend.</summary>
public class WslContainerException : ContainerException
{
	public WslContainerException(string message)
		: base(message) { }

	public WslContainerException(string message, Exception innerException)
		: base(message, innerException) { }
}

/// <summary>WSL or WSL Containers prerequisites are missing.</summary>
public class WslContainerPrerequisiteException(string message) : ContainerPrerequisiteException(message) { }

/// <summary>Starting the container failed inside WSLC (image, OCI runtime, or environment).</summary>
public class WslContainerStartupException(string message, Exception innerException)
	: ContainerStartupException(message, innerException) { }

/// <summary>The backing WSLC session terminated unexpectedly.</summary>
public class WslContainerSessionTerminatedException(string message) : WslContainerException(message) { }
