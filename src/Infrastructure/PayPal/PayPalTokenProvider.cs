using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token via the client-credentials grant.
/// Registered as a singleton so the token is shared and reused until shortly before it expires.
/// </summary>
public class PayPalTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalTokenProvider> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory,
        PayPalSettings settings,
        ILogger<PayPalTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Refresh a minute before the reported expiry to avoid races on the boundary.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-1))
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-1))
            {
                return _cachedToken;
            }

            _settings.Validate();

            var client = _httpClientFactory.CreateClient(PayPalGateway.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");

            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Never log the body verbatim (it may echo credentials); log status only.
                _logger.LogError("PayPal token request failed with status {Status}.", (int)response.StatusCode);
                throw new PayPalException(
                    $"Could not obtain a PayPal access token (HTTP {(int)response.StatusCode}). " +
                    "Verify PayPal:ClientId / PayPal:ClientSecret and PayPal:Environment.");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString()
                ?? throw new PayPalException("PayPal token response did not contain an access_token.");
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;

            _cachedToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
