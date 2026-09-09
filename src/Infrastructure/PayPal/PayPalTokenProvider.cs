using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token using the client-credentials flow described
/// by the specs' <c>Oauth2</c> security scheme (token URL <c>/v1/oauth2/token</c>). The token is
/// reused until shortly before it expires.
/// </summary>
public class PayPalTokenProvider
{
    /// <summary>Named HttpClient configured with the PayPal API base address.</summary>
    public const string HttpClientName = "PayPal";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, PayPalSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        // A 60s safety margin avoids using a token that expires mid-request.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
        {
            return _cachedToken;
        }

        await _mutex.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException((int)response.StatusCode, "TOKEN_REQUEST_FAILED",
                    $"Failed to obtain PayPal access token: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString()
                ?? throw new PayPalApiException(500, "TOKEN_REQUEST_FAILED", "PayPal returned no access token.");
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            _cachedToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return token;
        }
        finally
        {
            _mutex.Release();
        }
    }
}
