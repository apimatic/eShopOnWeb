using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public interface IPayPalTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Obtains and caches an OAuth 2.0 access token using the client-credentials grant defined by the PayPal specs
/// (<c>tokenUrl: /v1/oauth2/token</c>, HTTP Basic auth with the client id/secret). The token is reused until shortly
/// before it expires. Registered as a singleton so the cache is shared across requests.
/// </summary>
public class PayPalTokenProvider : IPayPalTokenProvider
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

            _options.Validate();

            var client = _httpClientFactory.CreateClient(PayPalHttpDefaults.ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException((int)response.StatusCode, "TOKEN_REQUEST_FAILED",
                    $"Failed to obtain a PayPal access token (HTTP {(int)response.StatusCode}).", null, Array.Empty<string>());
            }

            var token = System.Text.Json.JsonSerializer.Deserialize<PpTokenResponse>(body, PayPalHttpDefaults.JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new PayPalApiException((int)response.StatusCode, "TOKEN_REQUEST_FAILED",
                    "PayPal token response did not contain an access_token.", null, Array.Empty<string>());
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
}
