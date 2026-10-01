using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Purview.Containers.Waiting;

/// <summary>Waits until an HTTP(S) request against the mapped host port succeeds.</summary>
[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Transient network failures during startup are treated as not-ready."
)]
public sealed class HttpWaitStrategy(string path = "/") : WaitStrategy
{
	readonly string _path = string.IsNullOrEmpty(path) ? "/" : path;
	ushort? _containerPort;
	string _scheme = "http";
	HttpStatusCode? _expectedStatusCode = HttpStatusCode.OK;
	Func<int, bool>? _statusPredicate;
	readonly Dictionary<string, string> _headers = [with(StringComparer.OrdinalIgnoreCase)];
	bool _allowInsecureTls;
	readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(2);

	/// <summary>The container port to connect to. When unset, the first mapped port is used.</summary>
	public HttpWaitStrategy ForPort(ushort containerPort)
	{
		_containerPort = containerPort;
		return this;
	}

	/// <summary>The URL scheme: <c>http</c> or <c>https</c>.</summary>
	public HttpWaitStrategy ForScheme(string scheme)
	{
		_scheme = scheme;
		return this;
	}

	/// <summary>The expected HTTP status code.</summary>
	public HttpWaitStrategy ForStatusCode(HttpStatusCode statusCode)
	{
		_expectedStatusCode = statusCode;
		_statusPredicate = null;
		return this;
	}

	/// <summary>A predicate over the response status code.</summary>
	public HttpWaitStrategy ForStatusPredicate(Func<int, bool> predicate)
	{
		_statusPredicate = predicate;
		_expectedStatusCode = null;
		return this;
	}

	/// <summary>Adds a request header.</summary>
	public HttpWaitStrategy ForHeader(string name, string value)
	{
		_headers[name] = value;
		return this;
	}

	/// <summary>
	/// Disables TLS certificate validation for THIS request handler only (testing use; never global).
	/// </summary>
	public HttpWaitStrategy AllowInsecureTls()
	{
		_allowInsecureTls = true;
		return this;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		var hostPort = _containerPort is ushort containerPortValue
			? context.GetHostPort(containerPortValue)
			: context.FirstHostPort;
		if (hostPort is null)
		{
			return false;
		}

		var url = $"{_scheme}://127.0.0.1:{hostPort}{_path}";
		try
		{
			using SocketsHttpHandler handler = new()
			{
				ConnectTimeout = _requestTimeout,
				PooledConnectionLifetime = TimeSpan.Zero,
			};
			if (_allowInsecureTls)
			{
#pragma warning disable CA5359 // Do Not Disable Certificate Validation
				handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
#pragma warning restore CA5359 // Do Not Disable Certificate Validation
			}

			using HttpClient client = new(handler) { Timeout = _requestTimeout };
			using HttpRequestMessage request = new(HttpMethod.Get, url);
			foreach ((var name, var value) in _headers)
			{
				request.Headers.TryAddWithoutValidation(name, value);
			}

			using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
			var status = (int)response.StatusCode;
			return _statusPredicate?.Invoke(status)
				?? (_expectedStatusCode is HttpStatusCode expected && status == (int)expected);
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
