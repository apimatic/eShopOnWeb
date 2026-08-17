using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Fetches and caches a PayPal OAuth2 access token (client-credentials grant). Registered as a singleton so
/// the token is shared and refreshed proactively — never fetched per request. The caller passes the
/// PayPal-configured <see cref="HttpClient"/> so the token request hits the same base address as API calls.
/// </summary>
public sealed class PayPalTokenProvider
{
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(PayPalSettings settings, ILogger<PayPalTokenProvider> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(HttpClient http, CancellationToken ct = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _accessToken;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal OAuth token request failed with status {Status}.", (int)response.StatusCode);
                throw new PayPalApiException($"PayPal authentication failed (HTTP {(int)response.StatusCode}).", (int)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3000;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));

            return _accessToken!;
        }
        finally
        {
            _gate.Release();
        }
    }
}
