using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;

namespace Purview.WslContainers;

/// <summary>Waits until an HTTP(S) request against the mapped host port succeeds.</summary>
[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Transient network failures during startup are treated as not-ready."
)]
public sealed class HttpWaitStrategy : WaitStrategy
{
	private readonly string path;
	private ushort? containerPort;
	private string scheme = "http";
	private HttpStatusCode? expectedStatusCode = HttpStatusCode.OK;
	private Func<int, bool>? statusPredicate;
	private readonly Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
	private bool allowInsecureTls;
	private TimeSpan requestTimeout = TimeSpan.FromSeconds(2);

	public HttpWaitStrategy(string path = "/")
	{
		this.path = string.IsNullOrEmpty(path) ? "/" : path;
	}

	/// <summary>The container port to connect to. When unset, the first mapped port is used.</summary>
	public HttpWaitStrategy ForPort(ushort containerPort)
	{
		this.containerPort = containerPort;
		return this;
	}

	/// <summary>The URL scheme: <c>http</c> or <c>https</c>.</summary>
	public HttpWaitStrategy ForScheme(string scheme)
	{
		this.scheme = scheme;
		return this;
	}

	/// <summary>The expected HTTP status code.</summary>
	public HttpWaitStrategy ForStatusCode(HttpStatusCode statusCode)
	{
		expectedStatusCode = statusCode;
		statusPredicate = null;
		return this;
	}

	/// <summary>A predicate over the response status code.</summary>
	public HttpWaitStrategy ForStatusPredicate(Func<int, bool> predicate)
	{
		statusPredicate = predicate;
		expectedStatusCode = null;
		return this;
	}

	/// <summary>Adds a request header.</summary>
	public HttpWaitStrategy ForHeader(string name, string value)
	{
		headers[name] = value;
		return this;
	}

	/// <summary>
	/// Disables TLS certificate validation for THIS request handler only (testing use; never global).
	/// </summary>
	public HttpWaitStrategy AllowInsecureTls()
	{
		allowInsecureTls = true;
		return this;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		int? hostPort = containerPort is ushort containerPortValue
			? context.GetHostPort(containerPortValue)
			: context.FirstHostPort;
		if (hostPort is null)
		{
			return false;
		}

		string url = $"{scheme}://127.0.0.1:{hostPort}{path}";
		try
		{
			using SocketsHttpHandler handler = new SocketsHttpHandler
			{
				ConnectTimeout = requestTimeout,
				PooledConnectionLifetime = TimeSpan.Zero,
			};
			if (allowInsecureTls)
			{
				handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
			}

			using HttpClient client = new HttpClient(handler) { Timeout = requestTimeout };
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
			foreach ((string name, string value) in headers)
			{
				request.Headers.TryAddWithoutValidation(name, value);
			}

			using HttpResponseMessage response = await client
				.SendAsync(request, cancellationToken)
				.ConfigureAwait(false);
			int status = (int)response.StatusCode;
			return statusPredicate?.Invoke(status)
				?? (expectedStatusCode is HttpStatusCode expected && status == (int)expected);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			return false;
		}
	}
}
