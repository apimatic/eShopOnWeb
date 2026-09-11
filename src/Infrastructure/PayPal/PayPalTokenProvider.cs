using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token using the client-credentials flow defined by the
/// spec (POST /v1/oauth2/token, HTTP Basic client_id:secret). The token is reused until shortly
/// before it expires.
/// </summary>
public class PayPalTokenProvider
{
    private const string CacheKey = "paypal:access_token";
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly PayPalSettings _settings;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, IMemoryCache cache, IOptions<PayPalSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _settings = settings.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue<string>(CacheKey, out var cached) && !string.IsNullOrEmpty(cached))
            return cached!;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue<string>(CacheKey, out var cached2) && !string.IsNullOrEmpty(cached2))
                return cached2!;

            var token = await RequestTokenAsync(ct);
            return token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string> RequestTokenAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClientNames.Auth);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new PaymentGatewayException(
                $"Failed to obtain a PayPal access token ({(int)response.StatusCode}). Check PayPal:ClientId/ClientSecret/BaseUrl.",
                processorIssue: "AUTH_FAILURE", statusCode: (int)response.StatusCode);
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            throw new PaymentGatewayException("PayPal returned an empty access token.", processorIssue: "AUTH_FAILURE");

        var lifetime = TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 60));
        _cache.Set(CacheKey, token.AccessToken, lifetime);
        return token.AccessToken;
    }
}
