using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Acquires and caches a PayPal OAuth2 client-credentials access token. The token endpoint
/// (<c>/v1/oauth2/token</c>) and auth scheme (Basic client id/secret) come from the OpenAPI
/// security scheme; the request honours the <c>PayPal:BaseUrl</c> override like every other call.
/// A single token is shared and refreshed shortly before it expires.
/// </summary>
public class PayPalTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            var client = _httpClientFactory.CreateClient(PayPalClient.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
            });

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException(
                    $"PayPal token request failed ({(int)response.StatusCode}). Verify PayPal:ClientId / PayPal:ClientSecret / PayPal:Environment.",
                    (int)response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PpTokenResponse>(body);
            if (token?.AccessToken is null)
            {
                throw new PayPalApiException("PayPal token response did not contain an access_token.", (int)response.StatusCode);
            }

            _cachedToken = token.AccessToken;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Drop the cached token so the next call re-authenticates (used after a 401).</summary>
    public void Invalidate()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }
}
