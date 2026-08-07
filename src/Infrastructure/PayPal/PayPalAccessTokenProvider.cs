using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token using the client-credentials flow declared by the
/// PayPal specs (<c>tokenUrl: /v1/oauth2/token</c>). The token is cached until shortly before it
/// expires and refreshed under a lock so concurrent callers share a single token request.
/// Registered as a singleton so the cache is shared process-wide.
/// </summary>
public sealed class PayPalAccessTokenProvider
{
    public const string HttpClientName = "PayPal.Auth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

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
        // Refresh a minute early to avoid using a token that expires mid-request.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
        {
            return _cachedToken;
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
            {
                return _cachedToken;
            }

            var token = await RequestTokenAsync(cancellationToken);
            _cachedToken = token.AccessToken;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
            return _cachedToken!;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<OAuthTokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PayPalApiException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (e.g. via user-secrets).");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var tokenUrl = $"{_options.ResolveBaseUrl()}/v1/oauth2/token";

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("PayPal OAuth token request failed with status {StatusCode}.", (int)response.StatusCode);
            throw new PayPalApiException(
                $"PayPal OAuth token request failed with status {(int)response.StatusCode}. {Truncate(body)}",
                (int)response.StatusCode);
        }

        var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(PayPalJson.Options, cancellationToken);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PayPalApiException("PayPal OAuth token response did not contain an access token.");
        }

        return token;
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value.Substring(0, 500);
}
