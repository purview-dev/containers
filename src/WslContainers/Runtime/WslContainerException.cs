namespace Purview.WslContainers;

/// <summary>Base exception for all errors raised by the WSL Containers runtime.</summary>
public class WslContainerException : Exception
{
	public WslContainerException(string message)
		: base(message) { }

	public WslContainerException(string message, Exception innerException)
		: base(message, innerException) { }
}

/// <summary>Invalid container configuration detected before any WSLC operation.</summary>
public class WslContainerConfigurationException : WslContainerException
{
	public WslContainerConfigurationException(string message)
		: base(message) { }

	public WslContainerConfigurationException(string message, Exception innerException)
		: base(message, innerException) { }
}

/// <summary>WSL/WSL Containers prerequisites are missing or incompatible.</summary>
public class WslContainerPrerequisiteException : WslContainerException
{
	public WslContainerPrerequisiteException(string message)
		: base(message) { }
}

/// <summary>A requested feature is not supported by the current WSLC API.</summary>
public class WslContainerNotSupportedException : WslContainerException
{
	public WslContainerNotSupportedException(string message)
		: base(message) { }
}

/// <summary>Starting the container failed (image, OCI runtime, or environment).</summary>
public class WslContainerStartupException : WslContainerException
{
	public WslContainerStartupException(string message, Exception innerException)
		: base(message, innerException) { }
}

/// <summary>An operation did not complete within its configured timeout.</summary>
public class WslContainerTimeoutException : WslContainerException
{
	public WslContainerTimeoutException(string message)
		: base(message) { }
}

/// <summary>The backing WSLC session terminated unexpectedly.</summary>
public class WslContainerSessionTerminatedException : WslContainerException
{
	public WslContainerSessionTerminatedException(string message)
		: base(message) { }
}
