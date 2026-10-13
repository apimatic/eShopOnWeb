using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

/// <summary>
/// Supplies an OAuth access token for Walmart's seller-direct (<c>client_credentials</c>) flow,
/// per api-specs/walmart/authorization.json. Tokens are cached and reused until shortly before
/// they expire; the client secret and the token itself are never logged.
/// </summary>
public interface IWalmartAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

internal sealed class WalmartAccessTokenProvider : IWalmartAccessTokenProvider
{
    // Re-mint a little before the stated expiry to avoid racing the clock on in-flight calls.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WalmartSettings _settings;
    private readonly WalmartUrlResolver _urlResolver;
    private readonly ILogger<WalmartAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public WalmartAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<WalmartSettings> settings,
        ILogger<WalmartAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _urlResolver = new WalmartUrlResolver(_settings.BaseUrl);
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCached(out var cached))
        {
            return cached;
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(out cached))
            {
                return cached;
            }

            var token = await RequestTokenAsync(cancellationToken);
            return token;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private bool TryGetCached(out string token)
    {
        var current = _cachedToken;
        if (current is not null && DateTimeOffset.UtcNow < _expiresAt - ExpirySkew)
        {
            token = current;
            return true;
        }
        token = string.Empty;
        return false;
    }

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new WalmartApiException(
                "Walmart credentials are not configured. Set Walmart:ClientId and Walmart:ClientSecret.");
        }

        var requestUri = _urlResolver.Resolve(WalmartContract.AuthServer, WalmartContract.TokenPath);

        // client_credentials grant with client_secret_post body (authorization.json).
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _settings.ClientId!),
                new KeyValuePair<string, string>("client_secret", _settings.ClientSecret!),
            })
        };
        request.Headers.Accept.ParseAdd("application/json");

        var client = _httpClientFactory.CreateClient(WalmartHttpClient.Name);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WalmartApiException("Failed to reach the Walmart authorization endpoint.", inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryReadErrorAsync(response, cancellationToken);
                throw new WalmartApiException(
                    $"Walmart token request failed ({(int)response.StatusCode} {response.StatusCode}){error}.",
                    response.StatusCode);
            }

            TokenResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw new WalmartApiException("Walmart token response could not be parsed.", response.StatusCode, ex);
            }

            if (body is null || string.IsNullOrEmpty(body.AccessToken))
            {
                throw new WalmartApiException("Walmart token response did not contain an access token.", response.StatusCode);
            }

            var lifetime = body.ExpiresIn > 0 ? TimeSpan.FromSeconds(body.ExpiresIn) : TimeSpan.FromMinutes(15);
            _cachedToken = body.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow + lifetime;

            _logger.LogInformation("Obtained a Walmart access token (valid for {Seconds}s).", (int)lifetime.TotalSeconds);
            return body.AccessToken;
        }
    }

    private static async Task<string> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Surface only the OAuth error code, never any response body that might echo credentials.
        try
        {
            var error = await response.Content.ReadFromJsonAsync<OAuthError>(cancellationToken: cancellationToken);
            return !string.IsNullOrEmpty(error?.Error) ? $": {error!.Error}" : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
