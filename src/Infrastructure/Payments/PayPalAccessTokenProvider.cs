using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PayPalOptions> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public PayPalAccessTokenProvider(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (HasValidToken())
        {
            return _accessToken!;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (HasValidToken())
            {
                return _accessToken!;
            }

            var options = _options.Value;
            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new PaymentException(500, "PayPal client credentials are not configured.");
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentException((int)response.StatusCode, "PayPal authentication failed.");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body)
                ?? throw new PaymentException(502, "PayPal authentication returned an empty token.");

            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PaymentException(502, "PayPal authentication returned an empty token.");
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

    private bool HasValidToken() =>
        !string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _expiresAt;
}
