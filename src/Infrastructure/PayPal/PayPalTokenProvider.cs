using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Acquires and caches a PayPal OAuth2 access token via the client-credentials flow described by
/// the specs' <c>Oauth2</c> security scheme (token URL <c>/v1/oauth2/token</c>). The token is cached
/// process-wide and refreshed shortly before it expires; refreshes are serialized so a burst of
/// requests triggers at most one token call.
/// </summary>
public sealed class PayPalTokenProvider
{
    private const string TokenPath = "/v1/oauth2/token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, PayPalSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        // Fast path: a token that is still comfortably valid.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
            {
                return _cachedToken;
            }

            var token = await RequestTokenAsync(ct);
            _cachedToken = token.AccessToken;
            // Refresh a minute before the real expiry to avoid using an about-to-expire token.
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
            return _cachedToken!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<TokenResponse> RequestTokenAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttp.ClientName);
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            })
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {basic}");

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw PayPalHttp.BuildException((int)response.StatusCode, body);
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(body, PayPalHttp.JsonOptions);
        if (token?.AccessToken is null)
        {
            throw new PayPalApiException((int)response.StatusCode, "invalid_token_response",
                "PayPal did not return an access token.", null, null);
        }
        return token;
    }
}
