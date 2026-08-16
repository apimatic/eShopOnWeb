using System;
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
/// Acquires and caches a PayPal OAuth 2.0 access token (client-credentials flow). The token is reused
/// until shortly before it expires, then refreshed — per-request token fetches add latency and risk
/// rate limits. Thread-safe: concurrent callers share one refresh.
/// </summary>
public class PayPalTokenProvider
{
    public const string HttpClientName = "PayPal";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Refresh a minute early so an in-flight request never uses a just-expired token.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
            return _cachedToken;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
                return _cachedToken;

            return await RefreshAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string> RefreshAsync(CancellationToken cancellationToken)
    {
        var url = $"{_settings.ResolveBaseUrl()}/v1/oauth2/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids)
                ? string.Join(",", ids) : null;
            throw new PayPalApiException((int)response.StatusCode, "AUTH_FAILED", debugId,
                "Could not obtain a PayPal access token. Check PayPal:ClientId / PayPal:ClientSecret / PayPal:Environment.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()
            ?? throw new PayPalApiException((int)response.StatusCode, "AUTH_FAILED", null,
                "PayPal token response did not contain an access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        _cachedToken = token;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        return token;
    }
}
