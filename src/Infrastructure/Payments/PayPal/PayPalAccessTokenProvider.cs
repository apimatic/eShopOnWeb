using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
                throw new InvalidOperationException("PayPal ClientId and ClientSecret must be configured.");
            }

            var client = _httpClientFactory.CreateClient(PayPalClient.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            _logger.LogInformation("Requesting PayPal OAuth token from {Path}", "/v1/oauth2/token");
            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw PayPalClient.CreateApiException(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, PayPalClient.JsonOptions)
                        ?? throw new InvalidOperationException("PayPal token response was empty.");
            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new InvalidOperationException("PayPal token response did not include an access token.");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime, 30));
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
