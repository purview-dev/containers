using Azure.Core;

namespace Purview.Containers.AzureKeyVaultEmulator;

/// <summary>
/// Token credential backed by the emulator's own token endpoint. The emulator issues an unconditional JWT
/// and its JwtBearer pipeline does not validate it, so the token is fetched once and cached for the
/// container's lifetime.
/// </summary>
sealed class AzureKeyVaultEmulatorTokenCredential(HttpClient httpClient, Uri vaultUri) : TokenCredential
{
	readonly Uri _tokenUri = new(vaultUri, "/token");
	string? _token;

	/// <inheritdoc />
	public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
	{
		// Someone, somewhere, is using the synchronous client: the fetch is a single cached HTTPS call.
		return GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();
	}

	/// <inheritdoc />
	public override async ValueTask<AccessToken> GetTokenAsync(
		TokenRequestContext requestContext,
		CancellationToken cancellationToken
	)
	{
		if (_token is { } cached)
		{
			return new AccessToken(cached, DateTimeOffset.UtcNow.AddHours(1));
		}

		using HttpRequestMessage request = new(HttpMethod.Get, _tokenUri);
		using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		var token = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
		if (string.IsNullOrEmpty(token))
		{
			throw new InvalidOperationException($"The emulator returned an empty token from '{_tokenUri}'.");
		}

		_token = token;
		return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
	}
}
