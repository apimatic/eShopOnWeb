using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalAccessTokenService
{
    private const string CacheKey = "paypal-access-token";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PayPalAccessTokenService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<PayPalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
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

            var client = _httpClientFactory.CreateClient("PayPal");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ApplicationCore.Exceptions.PaymentGatewayException(
                    $"PayPal token request failed with {(int)response.StatusCode}.",
                    debugId: null);
            }

            var parsed = JsonSerializer.Deserialize<PayPalTokenResponse>(body)
                         ?? throw new ApplicationCore.Exceptions.PaymentGatewayException("PayPal token response was empty.");
            if (string.IsNullOrEmpty(parsed.AccessToken))
            {
                throw new ApplicationCore.Exceptions.PaymentGatewayException("PayPal token response did not include access_token.");
            }

            var lifetime = parsed.ExpiresIn > 60 ? parsed.ExpiresIn - 60 : parsed.ExpiresIn;
            _cache.Set(CacheKey, parsed.AccessToken, TimeSpan.FromSeconds(Math.Max(lifetime, 30)));
            return parsed.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
