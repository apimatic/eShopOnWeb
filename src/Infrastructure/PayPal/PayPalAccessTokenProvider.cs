using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.PayPal.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public interface IPayPalAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    void Invalidate();
}

public sealed class PayPalAccessTokenProvider : IPayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PayPalOptions> _options;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public PayPalAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<PayPalOptions> options,
        ILogger<PayPalAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
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

            var options = _options.CurrentValue;
            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new PaymentException("PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.", 500);
            }

            var client = _httpClientFactory.CreateClient(nameof(PayPalPaymentsClient));
            var tokenUrl = $"{options.GetApiBaseUrl()}/v1/oauth2/token";
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with {StatusCode}. Debug details omitted.", (int)response.StatusCode);
                throw new PaymentException("Unable to authenticate with PayPal. Check ClientId, ClientSecret, Environment, and BaseUrl.", 502);
            }

            var parsed = JsonSerializer.Deserialize<PayPalTokenResponseDto>(body, PayPalJson.Options);
            if (parsed?.AccessToken is null)
            {
                throw new PaymentException("PayPal token response did not include an access_token.", 502);
            }

            _accessToken = parsed.AccessToken;
            var lifetime = parsed.ExpiresIn > 60 ? parsed.ExpiresIn - 60 : parsed.ExpiresIn;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime, 1));
            _logger.LogInformation("Obtained a PayPal access token that expires in {ExpiresIn} seconds.", parsed.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _accessToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }
}
