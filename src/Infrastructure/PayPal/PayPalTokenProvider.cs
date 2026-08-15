using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches an OAuth 2.0 access token from PayPal using the client-credentials grant, per
/// the specs' <c>Oauth2</c> security scheme (token endpoint <c>/v1/oauth2/token</c>). The token
/// endpoint uses HTTP Basic auth with the REST client id/secret.
/// </summary>
public class PayPalTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalTokenProvider> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalSettings> settings,
        ILogger<PayPalTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: a still-valid cached token.
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

            var token = await RequestTokenAsync(cancellationToken);
            _cachedToken = token.AccessToken;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
            return _cachedToken!;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Drops the cached token so the next call re-authenticates (used after a 401).</summary>
    public void Invalidate()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    private async Task<TokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
    {
        _settings.Validate();

        var client = _httpClientFactory.CreateClient(PayPalHttpClientNames.Api);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayPal token request failed with status {StatusCode}.", (int)response.StatusCode);
            throw new PaymentGatewayException(
                "Failed to obtain a PayPal access token. Check PayPal:ClientId/ClientSecret.",
                (int)response.StatusCode,
                errorName: "TOKEN_REQUEST_FAILED");
        }

        var token = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(body, PayPalJson.Options);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PaymentGatewayException("PayPal token response did not contain an access_token.", (int)response.StatusCode);
        }

        return token;
    }
}
