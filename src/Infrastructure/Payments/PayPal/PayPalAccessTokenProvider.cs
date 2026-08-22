using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PayPalOptions> _options;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalOptions> options,
        ILogger<PayPalAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _accessToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _accessToken;
            }

            var options = _options.Value;
            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new PaymentConfigurationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            using var request = new HttpRequestMessage(HttpMethod.Post, Combine(options.ResolveBaseUrl(), "/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with {StatusCode}", (int)response.StatusCode);
                throw new PaymentGatewayException(
                    "Unable to authenticate with PayPal. Check PayPal:ClientId and PayPal:ClientSecret.",
                    (int)response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PayPalOAuthTokenResponse>(body, PayPalJson.Options);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new PaymentGatewayException("PayPal token response did not include an access_token.");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Combine(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}{path}";
}
