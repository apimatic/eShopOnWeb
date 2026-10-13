using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Obtains and caches a Walmart OAuth access token using the client-credentials grant described in
/// api-specs/walmart/authorization.json (POST /auth/v4/token). Tokens are cached in-memory until
/// shortly before they expire; the client secret and the access token are never logged.
/// </summary>
public interface IWalmartTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    void Invalidate();
}

public class WalmartTokenProvider : IWalmartTokenProvider
{
    public const string HttpClientName = "Walmart.Auth";
    private const string CacheKey = "Walmart.AccessToken";
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly WalmartSettings _settings;
    private readonly ILogger<WalmartTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public WalmartTokenProvider(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<WalmartSettings> settings,
        ILogger<WalmartTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public void Invalidate() => _cache.Remove(CacheKey);

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached!;
            }

            var (token, expiresIn) = await RequestTokenAsync(cancellationToken);
            var lifetime = TimeSpan.FromSeconds(Math.Max(expiresIn, 1));
            var cacheFor = lifetime > ExpirySkew ? lifetime - ExpirySkew : lifetime;
            _cache.Set(CacheKey, token, cacheFor);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(string token, int expiresIn)> RequestTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new WalmartApiException(
                "Walmart credentials are not configured. Set Walmart:ClientId and Walmart:ClientSecret.");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _settings.ClientId!),
            new KeyValuePair<string, string>("client_secret", _settings.ClientSecret!),
        });

        _logger.LogInformation("Requesting Walmart access token (client_credentials).");

        HttpResponseMessage response;
        try
        {
            // Relative to the auth base address (".../seller/"): api-specs authorization.json path.
            response = await client.PostAsync("auth/v4/token", content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new WalmartApiException($"Unable to reach the Walmart token endpoint: {ex.Message}", inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var description = await ReadOAuthErrorAsync(response, cancellationToken);
            throw new WalmartApiException(
                $"Walmart token request failed ({(int)response.StatusCode} {response.StatusCode}): {description}",
                response.StatusCode);
        }

        WalmartTokenResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<WalmartTokenResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new WalmartApiException($"Walmart token response could not be parsed: {ex.Message}", response.StatusCode, ex);
        }

        if (body is null || string.IsNullOrEmpty(body.AccessToken))
        {
            throw new WalmartApiException("Walmart token response did not contain an access_token.", response.StatusCode);
        }

        return (body.AccessToken!, body.ExpiresIn);
    }

    private static async Task<string> ReadOAuthErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<WalmartOAuthError>(cancellationToken: cancellationToken);
            if (error is not null && !string.IsNullOrEmpty(error.Error))
            {
                return string.IsNullOrEmpty(error.ErrorDescription)
                    ? error.Error!
                    : $"{error.Error} - {error.ErrorDescription}";
            }
        }
        catch
        {
            // Fall through to a generic message; never surface raw bodies that could echo secrets.
        }

        return response.StatusCode == HttpStatusCode.Unauthorized
            ? "invalid_client"
            : "unexpected error";
    }
}
