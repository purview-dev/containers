using System.Net;
using System.Net.Sockets;

namespace Purview.WslContainers.Waiting;

/// <summary>Waits until a TCP connection to the mapped host port succeeds (IPv4 loopback).</summary>
public sealed class TcpPortWaitStrategy(ushort containerPort, TimeSpan connectTimeout) : WaitStrategy
{
	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		var hostPort = context.GetHostPort(containerPort);
		if (hostPort is null)
		{
			return false;
		}

		using CancellationTokenSource timeoutSource = new(connectTimeout);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
		try
		{
			using TcpClient client = new();
			await client.ConnectAsync(IPAddress.Loopback, hostPort.Value, linked.Token).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
		{
			return false;
		}
		catch (SocketException)
		{
			return false;
		}
	}
}
