using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalTokenProvider : IPayPalTokenProvider, IDisposable
{
    private readonly PayPalSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(PayPalSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient { BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/") };
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: a still-valid cached token (refresh a minute early).
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

            var (token, expiresInSeconds) = await FetchTokenAsync(cancellationToken);
            _cachedToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresInSeconds - 60));
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    private async Task<(string Token, int ExpiresIn)> FetchTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PayPalApiException((int)response.StatusCode, "oauth_error", null, Array.Empty<string>(),
                $"Failed to obtain PayPal access token (HTTP {(int)response.StatusCode}). " +
                "Verify PayPal:ClientId / PayPal:ClientSecret / PayPal:Environment.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()
            ?? throw new PayPalApiException((int)response.StatusCode, "oauth_error", null, Array.Empty<string>(),
                "PayPal token response did not contain an access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
            ? seconds
            : 28800;
        return (token, expiresIn);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _lock.Dispose();
    }
}
