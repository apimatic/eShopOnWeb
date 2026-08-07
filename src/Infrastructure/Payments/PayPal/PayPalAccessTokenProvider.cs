using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token (client-credentials grant). Registered as a
/// singleton so the token is shared and refreshed across requests rather than fetched every call.
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
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
        {
            return _cachedToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
            {
                return _cachedToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PaymentGatewayException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
            }

            var client = _httpClientFactory.CreateClient(PayPalConstants.HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Do not log the response body: it can echo credentials context. Log status only.
                _logger.LogError("PayPal OAuth token request failed with status {StatusCode}.", (int)response.StatusCode);
                throw new PaymentGatewayException(
                    $"Unable to authenticate with PayPal (status {(int)response.StatusCode}).");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var token = root.GetProperty("access_token").GetString()
                ?? throw new PaymentGatewayException("PayPal OAuth response did not contain an access token.");
            var expiresInSeconds = root.TryGetProperty("expires_in", out var expiresProperty)
                ? expiresProperty.GetInt32()
                : 300;

            _cachedToken = token;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresInSeconds - 60));

            _logger.LogInformation("Obtained new PayPal access token (valid for {Seconds}s).", expiresInSeconds);
            return token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
