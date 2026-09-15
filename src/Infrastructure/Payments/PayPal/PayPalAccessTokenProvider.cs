using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public PayPalAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalOptions> options,
        ILogger<PayPalAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
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

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PayPalGatewayException("PayPal client credentials are not configured.");
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            var tokenUrl = $"{_options.ResolveBaseUrl()}/v1/oauth2/token";
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            _logger.LogInformation("Requesting PayPal access token from {TokenUrl}", tokenUrl);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalGatewayException(
                    $"PayPal token request failed with status {(int)response.StatusCode}.",
                    (int)response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PayPalOAuthTokenResponse>(body, PayPalJson.Options);
            if (token?.AccessToken is null)
            {
                throw new PayPalGatewayException("PayPal token response did not include an access token.");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 120 ? token.ExpiresIn - 60 : Math.Max(token.ExpiresIn - 10, 30);
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
