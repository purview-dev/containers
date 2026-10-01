using System.Net;
using System.Net.Sockets;
using System.Text;
using Purview.Containers.Waiting;

namespace Purview.Containers.Wsl;

public class HttpWaitStrategyTests
{
	[Test]
	public async Task Http_ReturnsTrueOnExpectedStatus(CancellationToken cancellationToken)
	{
		(var port, var listener, var _) = StartServer(HttpStatusCode.OK, cancellationToken);
		try
		{
			await using FakeContainer container = new()
			{
				PortMappings = new Dictionary<ushort, ushort> { [80] = (ushort)port },
			};
			WaitContext context = new(container, container.PortMappings, networkIp: null);
			var strategy = Wait.ForHttp("/health").ForPort(80).ForStatusCode(HttpStatusCode.OK);

			await Assert.That(await strategy.UntilAsync(context, cancellationToken)).IsTrue();
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task Http_ReturnsFalseOnUnexpectedStatus(CancellationToken cancellationToken)
	{
		(var port, var listener, var _) = StartServer(HttpStatusCode.InternalServerError, cancellationToken);
		try
		{
			await using FakeContainer container = new()
			{
				PortMappings = new Dictionary<ushort, ushort> { [80] = (ushort)port },
			};
			WaitContext context = new(container, container.PortMappings, networkIp: null);
			var strategy = Wait.ForHttp("/health").ForPort(80).ForStatusCode(HttpStatusCode.OK);

			await Assert.That(await strategy.UntilAsync(context, cancellationToken)).IsFalse();
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task Http_StatusPredicate_IsUsed(CancellationToken cancellationToken)
	{
		(var port, var listener, var _) = StartServer(HttpStatusCode.Accepted, cancellationToken);
		try
		{
			await using FakeContainer container = new()
			{
				PortMappings = new Dictionary<ushort, ushort> { [80] = (ushort)port },
			};
			WaitContext context = new(container, container.PortMappings, networkIp: null);
			var strategy = Wait.ForHttp("/").ForPort(80).ForStatusPredicate(status => status is >= 200 and < 300);

			await Assert.That(await strategy.UntilAsync(context, cancellationToken)).IsTrue();
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task Http_ReturnsFalseWhenPortNotMapped(CancellationToken cancellationToken)
	{
		await using FakeContainer container = new();
		WaitContext context = new(container, container.PortMappings, networkIp: null);
		var strategy = Wait.ForHttp("/").ForPort(80);

		await Assert.That(await strategy.UntilAsync(context, cancellationToken)).IsFalse();
	}

	static (int Port, HttpListener Listener, Task Handler) StartServer(
		HttpStatusCode status,
		CancellationToken cancellationToken
	)
	{
		using TcpListener probe = new(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		HttpListener listener = new();
		listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		listener.Start();

		var handler = Task.Run(
			async () =>
			{
				while (listener.IsListening && !cancellationToken.IsCancellationRequested)
				{
					try
					{
						var ctx = await listener.GetContextAsync();
						ctx.Response.StatusCode = (int)status;
						var body = Encoding.UTF8.GetBytes("ok");
						ctx.Response.ContentLength64 = body.Length;
						await ctx.Response.OutputStream.WriteAsync(body, cancellationToken);
						ctx.Response.Close();
					}
					catch (HttpListenerException)
					{
						break;
					}
					catch (ObjectDisposedException)
					{
						break;
					}
				}
			},
			cancellationToken
		);

		return (port, listener, handler);
	}
}
