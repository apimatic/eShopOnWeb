using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PayPalOptions> _options;
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
                throw new CheckoutException(500, "PayPal:ClientId and PayPal:ClientSecret must be configured.");
            }

            var client = _httpClientFactory.CreateClient(nameof(PayPalAccessTokenProvider));
            var request = new HttpRequestMessage(HttpMethod.Post, PayPalUrl.Combine(PayPalUrl.ResolveBase(options), "/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with {StatusCode}.", (int)response.StatusCode);
                throw new PayPalApiException((int)response.StatusCode, "PayPal rejected the client credentials.");
            }

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.GetProperty("access_token").GetString();
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresEl) && expiresEl.TryGetInt32(out var seconds)
                ? seconds
                : 300;

            if (string.IsNullOrEmpty(token))
            {
                throw new PayPalApiException(502, "PayPal token response did not include an access_token.");
            }

            _accessToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
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
