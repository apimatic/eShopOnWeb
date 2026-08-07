using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Acquires and caches PayPal OAuth2 access tokens using the client-credentials grant, as
/// declared by the specs' security scheme (Oauth2, tokenUrl <c>/v1/oauth2/token</c>). Registered
/// as a singleton so the bearer token is shared and refreshed across requests.
/// </summary>
public sealed class PayPalAccessTokenProvider
{
    private const string TokenPath = "v1/oauth2/token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalSettings> settings,
        ILogger<PayPalAccessTokenProvider> logger)
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
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new PaymentProcessingException(
                "PayPal is not configured: PayPal:ClientId and PayPal:ClientSecret must be provided.");
        }

        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath);
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = PayPalErrorReader.Parse(body);
            _logger.LogWarning("PayPal OAuth token request failed ({0}): {1}",
                (int)response.StatusCode, error.Message);
            throw new PaymentProcessingException(
                $"Could not obtain a PayPal access token: {error.Message}", error.DebugId);
        }

        var token = PayPalJson.Deserialize<PayPalOAuthTokenResponse>(body);
        if (token?.AccessToken is null)
        {
            throw new PaymentProcessingException("PayPal returned an empty access token.");
        }

        // Refresh a minute early to avoid using a token that expires mid-request.
        var lifetime = Math.Max(token.ExpiresIn - 60, 30);
        _cachedToken = token.AccessToken;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);

        _logger.LogInformation("Obtained a PayPal access token (valid for ~{0}s).", token.ExpiresIn);
        return _cachedToken;
    }
}
