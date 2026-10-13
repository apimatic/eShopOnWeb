using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Acquires and caches a Walmart OAuth access token using the <c>client_credentials</c> grant
/// (authorization.json, POST /auth/v4/token). Tokens are short-lived; this caches the current one
/// until shortly before it expires and re-mints on demand. The client secret and the access token
/// are treated as secrets and never logged.
/// </summary>
public class WalmartTokenProvider
{
    // Named HttpClient used only for the token call — deliberately has no auth handler.
    public const string HttpClientName = "WalmartAuth";

    // Re-mint a little before expiry to avoid using a token that lapses mid-request.
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WalmartEndpointResolver _resolver;
    private readonly WalmartOptions _options;
    private readonly ILogger<WalmartTokenProvider> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public WalmartTokenProvider(
        IHttpClientFactory httpClientFactory,
        WalmartEndpointResolver resolver,
        IOptions<WalmartOptions> options,
        ILogger<WalmartTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedToken(out var cached))
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedToken(out cached))
            {
                return cached;
            }
            return await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Discards the cached token so the next request forces a re-mint (e.g. after a 401).</summary>
    public void Invalidate()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    private bool TryGetCachedToken(out string token)
    {
        var current = _cachedToken;
        if (current is not null && DateTimeOffset.UtcNow < _expiresAt - ExpiryBuffer)
        {
            token = current;
            return true;
        }
        token = string.Empty;
        return false;
    }

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        if (!_options.HasCredentials)
        {
            throw new WalmartApiException(
                "Walmart credentials are not configured. Set Walmart:ClientId and Walmart:ClientSecret.");
        }

        var tokenUrl = _resolver.TokenUrl();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _options.ClientId!),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret!),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new WalmartApiException($"Failed to reach the Walmart token endpoint at {tokenUrl.Scheme}://{tokenUrl.Authority}.", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string? errorCode = null;
                string detail = response.ReasonPhrase ?? response.StatusCode.ToString();
                try
                {
                    var err = await response.Content.ReadFromJsonAsync<WalmartOAuthError>(cancellationToken: cancellationToken);
                    if (err?.Error is not null)
                    {
                        errorCode = err.Error;
                        detail = err.ErrorDescription ?? err.Error;
                    }
                }
                catch
                {
                    // Non-JSON error body; fall back to status text. Never echo the body (may be large/noisy).
                }

                _logger.LogError("Walmart token request failed with status {StatusCode} ({ErrorCode}).",
                    (int)response.StatusCode, errorCode ?? "n/a");

                throw new WalmartApiException(
                    $"Walmart token request failed: {detail}.", response.StatusCode, errorCode);
            }

            var token = await response.Content.ReadFromJsonAsync<WalmartTokenResponse>(cancellationToken: cancellationToken);
            if (token?.AccessToken is null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new WalmartApiException("Walmart token response did not contain an access_token.", response.StatusCode);
            }

            _cachedToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 0 ? TimeSpan.FromSeconds(token.ExpiresIn) : TimeSpan.FromMinutes(15);
            _expiresAt = DateTimeOffset.UtcNow + lifetime;

            _logger.LogInformation("Obtained Walmart access token; expires in {Seconds}s.", (int)lifetime.TotalSeconds);
            return _cachedToken;
        }
    }
}
