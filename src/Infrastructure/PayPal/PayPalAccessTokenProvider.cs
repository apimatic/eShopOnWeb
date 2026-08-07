using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Acquires and caches a PayPal OAuth2 access token using the client-credentials flow declared by the
/// specs' security scheme (token endpoint <c>/v1/oauth2/token</c>, HTTP Basic auth with the app's
/// client id / secret). The token is cached and reused until shortly before it expires; concurrent
/// callers share a single refresh.
/// </summary>
public class PayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

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
        if (IsTokenValid())
        {
            return _cachedToken!;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsTokenValid())
            {
                return _cachedToken!;
            }

            return await RequestNewTokenAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsTokenValid() =>
        _cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc;

    private async Task<string> RequestNewTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new PaymentGatewayException(
                "PayPal client credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).");
        }

        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        var basic = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Do not log the body — an OAuth error body can echo request material.
            _logger.LogWarning($"PayPal token request failed with status {(int)response.StatusCode}.");
            throw new PaymentGatewayException(
                "Failed to obtain a PayPal access token.",
                httpStatusCode: (int)response.StatusCode);
        }

        var token = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(body);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PaymentGatewayException("PayPal returned an empty access token.");
        }

        // Refresh a minute before expiry to avoid using a token that lapses mid-request.
        var lifetimeSeconds = Math.Max(token.ExpiresIn - 60, 30);
        _cachedToken = token.AccessToken;
        _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds);

        _logger.LogInformation($"Acquired new PayPal access token (valid ~{token.ExpiresIn}s).");
        return _cachedToken;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
