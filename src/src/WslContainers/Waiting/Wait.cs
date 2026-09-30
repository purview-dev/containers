using System.Text.RegularExpressions;

namespace Purview.WslContainers.Waiting;

/// <summary>Factory for composable wait strategies.</summary>
public static class Wait
{
	/// <summary>Waits until the container init process is running. This only means the process started, not that a service is ready.</summary>
	public static WaitStrategy ForContainerRunning()
	{
		return new ContainerRunningWaitStrategy();
	}

	/// <summary>Waits until a TCP connection to the mapped host port succeeds.</summary>
	public static WaitStrategy ForTcpPort(ushort containerPort, TimeSpan? connectTimeout = null)
	{
		return new TcpPortWaitStrategy(containerPort, connectTimeout ?? TimeSpan.FromSeconds(2));
	}

	/// <summary>Waits until an HTTP(S) request succeeds. Configure with the returned builder.</summary>
	public static HttpWaitStrategy ForHttp(string path = "/")
	{
		return new HttpWaitStrategy(path);
	}

	/// <summary>Waits until the init-process output contains <paramref name="message" />.</summary>
	public static WaitStrategy ForLogMessage(string message)
	{
		return new LogMessageWaitStrategy(message);
	}

	/// <summary>Waits until the init-process output matches <paramref name="pattern" />.</summary>
	public static WaitStrategy ForLogMessage(Regex pattern)
	{
		return new LogMessageWaitStrategy(pattern);
	}

	/// <summary>Waits until a command executed inside the container exits with code 0.</summary>
	public static WaitStrategy ForCommand(params string[] command)
	{
		return new CommandWaitStrategy(command);
	}

	/// <summary>Waits until a custom predicate returns <c>true</c>.</summary>
	public static WaitStrategy ForCustom(Func<WaitContext, CancellationToken, Task<bool>> predicate)
	{
		return new CustomWaitStrategy(predicate);
	}

	/// <summary>Waits until every contained strategy succeeds.</summary>
	public static WaitStrategy ForAll(params IWaitStrategy[] strategies)
	{
		return new AllWaitStrategy(strategies);
	}

	/// <summary>Waits until any contained strategy succeeds.</summary>
	public static WaitStrategy ForAny(params IWaitStrategy[] strategies)
	{
		return new AnyWaitStrategy(strategies);
	}
}
