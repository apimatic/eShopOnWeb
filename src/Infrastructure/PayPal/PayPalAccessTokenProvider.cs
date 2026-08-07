using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Acquires and caches a PayPal OAuth2 access token using the client-credentials flow described by
/// the specs' security scheme (<c>POST /v1/oauth2/token</c>, HTTP Basic auth). The token is cached
/// across requests until shortly before it expires; concurrent callers share a single refresh.
/// </summary>
public sealed class PayPalAccessTokenProvider
{
    public const string HttpClientName = "PayPalAuth";
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

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
        if (IsCurrent())
        {
            return _cachedToken!;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsCurrent())
            {
                return _cachedToken!;
            }

            _options.Validate();

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Do not log the response body verbatim; it may echo request context.
                _logger.LogError("PayPal token request failed with status {StatusCode}.", (int)response.StatusCode);
                throw new PayPalApiException(
                    "Failed to authenticate with PayPal. Check the configured PayPal:ClientId / PayPal:ClientSecret.",
                    (int)response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body, PayPalJson.Options);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PayPalApiException("PayPal returned an empty access token.");
            }

            _cachedToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn) - ExpiryBuffer;
            return _cachedToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsCurrent() => _cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt;
}
