using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>Obtains and caches a Walmart OAuth bearer token (client_credentials grant).</summary>
public interface IWalmartTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Exchanges the configured client credentials for a Bearer access token at the contract's token
/// endpoint (<c>POST /auth/v4/token</c>, grant_type=client_credentials, client_secret_post), and
/// caches it until shortly before it expires. The client secret and the issued token are never logged.
/// </summary>
public class WalmartTokenProvider : IWalmartTokenProvider
{
    // Re-mint this many seconds before the stated expiry to avoid using a token that expires in flight.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WalmartSettings _settings;
    private readonly WalmartEndpoints _endpoints;
    private readonly ILogger<WalmartTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public WalmartTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<WalmartSettings> settings,
        ILogger<WalmartTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _endpoints = new WalmartEndpoints(_settings.BaseUrl);
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCached(out var cached))
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while we waited for the lock.
            if (TryGetCached(out cached))
            {
                return cached;
            }

            return await RequestNewTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool TryGetCached(out string token)
    {
        var current = _cachedToken;
        if (current is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            token = current;
            return true;
        }
        token = string.Empty;
        return false;
    }

    private async Task<string> RequestNewTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new WalmartApiException(
                "Walmart credentials are not configured. Set Walmart:ClientId and Walmart:ClientSecret.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoints.TokenEndpoint)
        {
            // client_secret_post: credentials travel in the form body over TLS.
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret
            })
        };
        request.Headers.Accept.ParseAdd("application/json");

        _logger.LogDebug("Requesting Walmart access token from {TokenEndpoint}.", _endpoints.TokenEndpoint);

        var httpClient = _httpClientFactory.CreateClient(WalmartHttpClients.Auth);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await TryReadOAuthErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw new WalmartApiException(
                $"Walmart token request failed ({(int)response.StatusCode} {response.StatusCode}){error}.");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
        {
            throw new WalmartApiException("Walmart token response did not contain an access token.");
        }

        _cachedToken = payload.AccessToken;
        var lifetime = payload.ExpiresIn > 0 ? TimeSpan.FromSeconds(payload.ExpiresIn) : TimeSpan.FromMinutes(15);
        var effective = lifetime > ExpirySkew ? lifetime - ExpirySkew : lifetime;
        _expiresAt = DateTimeOffset.UtcNow.Add(effective);

        _logger.LogDebug("Obtained Walmart access token; valid for ~{Seconds}s.", (int)lifetime.TotalSeconds);
        return _cachedToken;
    }

    private static async Task<string> TryReadOAuthErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<OAuthError>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (error is not null && !string.IsNullOrEmpty(error.Error))
            {
                return string.IsNullOrEmpty(error.Description)
                    ? $": {error.Error}"
                    : $": {error.Error} - {error.Description}";
            }
        }
        catch
        {
            // Body was not a parseable OAuth error; fall through to the status-only message.
        }
        return string.Empty;
    }

    // Wire models from authorization.json (only the non-sensitive fields we act on are mapped).
    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class OAuthError
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        [JsonPropertyName("error_description")]
        public string? Description { get; set; }
    }
}
