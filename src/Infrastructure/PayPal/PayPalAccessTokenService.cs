using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.PayPal.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalAccessTokenService
{
    private const string CacheKey = "paypal-access-token";
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalAccessTokenService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PayPalAccessTokenService(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        IMemoryCache cache,
        ILogger<PayPalAccessTokenService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new OrderPaymentException("PayPal credentials are not configured.", 500, "PAYPAL_NOT_CONFIGURED");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            _logger.LogInformation("Requesting PayPal OAuth2 client-credentials token.");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with {StatusCode}.", (int)response.StatusCode);
                throw new OrderPaymentException("PayPal authentication failed.", 502, "PAYPAL_AUTH_FAILED");
            }

            var token = PayPalJson.Deserialize<PayPalOAuthTokenResponse>(body);
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new OrderPaymentException("PayPal authentication returned an empty token.", 502, "PAYPAL_AUTH_FAILED");
            }

            var lifetime = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 30));
            _cache.Set(CacheKey, token.AccessToken, lifetime);
            return token.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
