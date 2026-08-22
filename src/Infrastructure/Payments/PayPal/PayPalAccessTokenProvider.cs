using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public interface IPayPalAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class PayPalAccessTokenProvider : IPayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalOptions> options,
        ILogger<PayPalAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PayPalGatewayException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with {Status}.", (int)response.StatusCode);
                throw new PayPalGatewayException("PayPal rejected the credential/token request.")
                {
                    HttpStatus = (int)response.StatusCode
                };
            }

            var token = JsonSerializer.Deserialize<PayPalOAuthTokenDto>(body, PayPalJson.Options);
            if (token?.AccessToken is null)
            {
                throw new PayPalGatewayException("PayPal token response did not include an access_token.");
            }

            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
            _cachedToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime, 30));
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
