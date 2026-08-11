using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token via the client-credentials grant defined in
/// the spec (<c>POST /v1/oauth2/token</c>). The token is reused until shortly before it expires.
/// The token request goes to the same base address as every other call, honouring PayPal:BaseUrl.
/// </summary>
public class PayPalTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            var client = _httpClientFactory.CreateClient(PayPalClient.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new PayPalApiException(response.StatusCode, "AUTH_FAILED",
                    $"Failed to obtain a PayPal access token. Response: {Truncate(body)}", null, null);
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(
                PayPalClient.JsonOptions, cancellationToken);
            if (token?.AccessToken is null)
            {
                throw new PayPalApiException(response.StatusCode, "AUTH_FAILED",
                    "PayPal token response did not contain an access_token.", null, null);
            }

            _cachedToken = token.AccessToken;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            var lifetime = Math.Max(token.ExpiresIn - 60, 30);
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
