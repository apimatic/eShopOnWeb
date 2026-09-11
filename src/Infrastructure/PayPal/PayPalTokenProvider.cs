using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token using the client-credentials flow described by
/// the specs' security scheme (<c>POST /v1/oauth2/token</c>, HTTP Basic with the client id/secret).
/// A single cached token is shared and refreshed shortly before it expires. Registered as a
/// singleton so the token survives across requests.
/// </summary>
public class PayPalTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, PayPalOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Refresh a minute early to avoid using a token that expires mid-request.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt - TimeSpan.FromMinutes(1))
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt - TimeSpan.FromMinutes(1))
            {
                return _cachedToken;
            }

            var token = await RequestTokenAsync(cancellationToken);
            _cachedToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return _cachedToken!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<TokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(PayPalHttp.TokenClientName);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.ResolvedBaseUrl}/v1/oauth2/token");

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PayPalApiException(response.StatusCode, "authentication_error",
                $"Failed to obtain PayPal access token (HTTP {(int)response.StatusCode}): {body}",
                Array.Empty<PayPalErrorIssue>(), null);
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(body);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PayPalApiException(response.StatusCode, "authentication_error",
                "PayPal token response did not contain an access token.", Array.Empty<PayPalErrorIssue>(), null);
        }

        return token;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
