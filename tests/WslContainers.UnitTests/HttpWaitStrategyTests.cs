using System.Net;
using System.Net.Sockets;
using System.Text;
using Purview.WslContainers;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace WslContainers.UnitTests;

public class HttpWaitStrategyTests
{
	[Test]
	public async Task Http_ReturnsTrueOnExpectedStatus()
	{
		(int port, HttpListener listener, Task _) = StartServer(HttpStatusCode.OK);
		try
		{
			FakeContainer container = new FakeContainer
			{
				PortMappings = new Dictionary<ushort, ushort> { [80] = (ushort)port },
			};
			WaitContext context = new WaitContext(container, container.PortMappings, networkIp: null);
			HttpWaitStrategy strategy = Wait.ForHttp("/health").ForPort(80).ForStatusCode(HttpStatusCode.OK);

			await Assert.That(await strategy.UntilAsync(context, CancellationToken.None)).IsTrue();
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task Http_ReturnsFalseOnUnexpectedStatus()
	{
		(int port, HttpListener listener, Task _) = StartServer(HttpStatusCode.InternalServerError);
		try
		{
			FakeContainer container = new FakeContainer
			{
				PortMappings = new Dictionary<ushort, ushort> { [80] = (ushort)port },
			};
			WaitContext context = new WaitContext(container, container.PortMappings, networkIp: null);
			HttpWaitStrategy strategy = Wait.ForHttp("/health").ForPort(80).ForStatusCode(HttpStatusCode.OK);

			await Assert.That(await strategy.UntilAsync(context, CancellationToken.None)).IsFalse();
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task Http_StatusPredicate_IsUsed()
	{
		(int port, HttpListener listener, Task _) = StartServer(HttpStatusCode.Accepted);
		try
		{
			FakeContainer container = new FakeContainer
			{
				PortMappings = new Dictionary<ushort, ushort> { [80] = (ushort)port },
			};
			WaitContext context = new WaitContext(container, container.PortMappings, networkIp: null);
			HttpWaitStrategy strategy = Wait.ForHttp("/")
				.ForPort(80)
				.ForStatusPredicate(status => status >= 200 && status < 300);

			await Assert.That(await strategy.UntilAsync(context, CancellationToken.None)).IsTrue();
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task Http_ReturnsFalseWhenPortNotMapped()
	{
		FakeContainer container = new FakeContainer();
		WaitContext context = new WaitContext(container, container.PortMappings, networkIp: null);
		HttpWaitStrategy strategy = Wait.ForHttp("/").ForPort(80);

		await Assert.That(await strategy.UntilAsync(context, CancellationToken.None)).IsFalse();
	}

	private static (int Port, HttpListener Listener, Task Handler) StartServer(HttpStatusCode status)
	{
		using TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		int port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		HttpListener listener = new HttpListener();
		listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		listener.Start();

		Task handler = Task.Run(async () =>
		{
			while (listener.IsListening)
			{
				try
				{
					HttpListenerContext ctx = await listener.GetContextAsync();
					ctx.Response.StatusCode = (int)status;
					byte[] body = Encoding.UTF8.GetBytes("ok");
					ctx.Response.ContentLength64 = body.Length;
					await ctx.Response.OutputStream.WriteAsync(body);
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
		});

		return (port, listener, handler);
	}
}
