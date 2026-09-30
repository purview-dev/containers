using System.Net;
using System.Net.Sockets;

namespace Purview.WslContainers;

/// <summary>Waits until a TCP connection to the mapped host port succeeds (IPv4 loopback).</summary>
public sealed class TcpPortWaitStrategy : WaitStrategy
{
	private readonly ushort containerPort;
	private readonly TimeSpan connectTimeout;

	public TcpPortWaitStrategy(ushort containerPort, TimeSpan connectTimeout)
	{
		this.containerPort = containerPort;
		this.connectTimeout = connectTimeout;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		int? hostPort = context.GetHostPort(containerPort);
		if (hostPort is null)
		{
			return false;
		}

		using CancellationTokenSource timeoutSource = new CancellationTokenSource(connectTimeout);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			timeoutSource.Token
		);
		try
		{
			using TcpClient client = new TcpClient();
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
