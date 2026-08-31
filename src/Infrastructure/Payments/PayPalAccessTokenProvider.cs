using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Acquires and caches OAuth2 client-credentials access tokens from the PayPal
/// token endpoint (tokenUrl /v1/oauth2/token per the specs' Oauth2 security scheme).
/// </summary>
public class PayPalAccessTokenProvider
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly PaymentGatewayOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(HttpClient httpClient, PaymentGatewayOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentGatewayException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(environment variables PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET or user-secrets).");
            }

            var baseUrl = _options.ResolveBaseUrl();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentGatewayException($"PayPal token request failed with status {(int)response.StatusCode}. Check the configured credentials.");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new PaymentGatewayException("PayPal token request succeeded but returned no access token.");
            }

            _accessToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 300 : token.ExpiresIn) - ExpiryMargin;
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
