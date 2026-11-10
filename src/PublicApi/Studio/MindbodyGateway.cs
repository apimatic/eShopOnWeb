using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindbodyPublicApi;
using MindbodyPublicApi.Core.Configuration;
using MindbodyPublicApi.Core.ErrorResponse;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The single entry point to Mindbody (through the SDK). Owns the SDK client, keeps the staff user
/// token fresh for as long as the process lives, and translates failures into <see cref="StudioException"/>.
/// </summary>
public sealed class MindbodyGateway : IDisposable
{
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(10);

    private readonly MindbodySettings _settings;
    private readonly ILogger<MindbodyGateway> _logger;
    private readonly object _clientLock = new();
    private HttpClient? _httpClient;
    private MindbodyPublicApiClient? _client;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public MindbodyGateway(IOptions<MindbodySettings> settings, ILogger<MindbodyGateway> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string SiteId => Require(_settings.SiteId, "SiteId");

    public MindbodyPublicApiClient Client
    {
        get
        {
            if (_client is not null) return _client;
            lock (_clientLock)
            {
                if (_client is not null) return _client;

                var apiKey = Require(_settings.ApiKey, "ApiKey");
                Require(_settings.SiteId, "SiteId");
                Require(_settings.Username, "Username");
                Require(_settings.Password, "Password");

                var options = new MindbodyPublicApiClientOptions
                {
                    ApiKey = apiKey,
                    // Fail within a mobile-app-friendly time instead of the SDK's 100 s per attempt.
                    Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20), MaxRetries = 2 },
                };
                if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
                {
                    // Used verbatim as the API's base address.
                    options.Server.Default.Production.BaseUrl = _settings.BaseUrl!;
                }

                // A long-lived client must not pin DNS / connections forever (the app runs for weeks).
                _httpClient = new HttpClient(new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                });
                _client = new MindbodyPublicApiClient(_httpClient, options);
                return _client;
            }
        }
    }

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new StudioException(503, $"The studio integration is not configured (missing Mindbody:{name}).")
            : value!;

    /// <summary>Runs a Mindbody call with a valid staff token, renewing it when it is stale or rejected.</summary>
    public async Task<T> CallAsync<T>(Func<MindbodyPublicApiClient, string, Task<T>> operation)
    {
        var client = Client;
        var token = await GetTokenAsync(null).ConfigureAwait(false);
        try
        {
            try
            {
                return await operation(client, token).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                // The token was revoked or expired early: get a new one and try once more.
                token = await GetTokenAsync(token).ConfigureAwait(false);
                return await operation(client, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    private async Task<string> GetTokenAsync(string? rejectedToken)
    {
        if (IsUsable(_token, rejectedToken)) return _token!;

        await _tokenLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsUsable(_token, rejectedToken)) return _token!;

            var response = await Client.UserToken.IssueToken(new IssueTokenRequest
            {
                SiteId = SiteId,
                Body = new IssueRequest { Username = _settings.Username, Password = _settings.Password },
            }).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.AccessToken))
                throw new StudioException(502, "The studio service did not issue an access token.");

            _tokenExpires = response.Expires ?? DateTimeOffset.UtcNow.AddHours(1);
            _token = response.AccessToken;
            return _token!;
        }
        catch (Exception ex) when (Translate(ex) is { } translated)
        {
            throw translated;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool IsUsable(string? token, string? rejectedToken) =>
        token is not null && token != rejectedToken && DateTimeOffset.UtcNow < _tokenExpires - TokenRefreshMargin;

    private StudioException? Translate(Exception ex)
    {
        switch (ex)
        {
            case StudioException:
                return null; // already translated; let it propagate untouched
            case ResponseDeserializationException:
                // The studio answered but its answer cannot be read: that is on the studio's side, not the caller's.
                _logger.LogError(ex, "Unreadable Mindbody response");
                return new StudioException(502, "The studio service returned an unreadable answer.", ex);
            case ApiException api:
                var status = (int)api.StatusCode;
                var message = ExtractMessage(api);
                if (status is 401 or 403)
                {
                    _logger.LogError("Mindbody rejected the integration credentials ({Status}): {Message}", status, message);
                    return new StudioException(502, "The studio service rejected the integration's credentials.", ex);
                }
                if (status == 429)
                    return new StudioException(503, "The studio service is busy; try again shortly.", ex);
                if (status >= 500)
                {
                    _logger.LogWarning("Mindbody error {Status}: {Message}", status, message);
                    return new StudioException(502, "The studio service is unavailable; try again shortly.", ex);
                }
                _logger.LogWarning(ex, "Mindbody refused a request ({Status}) {Method} {Uri}: {Body}", status, api.Method, api.RequestUri,
                    ex is ApiException<RawError> r ? r.Error.ReadAsString() : "");
                return new StudioException(422, message, ex);
            case SdkTimeoutException:
                return new StudioException(504, "The studio service did not answer in time; try again.", ex);
            case SdkException:
            case HttpRequestException:
                _logger.LogWarning(ex, "Mindbody call failed");
                return new StudioException(502, "The studio service is unavailable; try again shortly.", ex);
            case JsonException:
                _logger.LogError(ex, "Unreadable Mindbody response");
                return new StudioException(502, "The studio service returned an unreadable answer.", ex);
            default:
                return null;
        }
    }

    private static string ExtractMessage(ApiException ex)
    {
        if (ex is ApiException<RawError> raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw.Error.ReadAsBytes());
                if (doc.RootElement.TryGetProperty("Error", out var error)
                    && error.TryGetProperty("Message", out var msg)
                    && msg.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(msg.GetString()))
                    return msg.GetString()!;
            }
            catch (JsonException)
            {
            }
        }
        return "The studio could not carry out the request.";
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _tokenLock.Dispose();
    }

    /// <summary>Reads every page of a paged Mindbody list.</summary>
    public static async Task<List<TItem>> ReadAllAsync<TPage, TItem>(
        Func<int, int, Task<TPage>> fetch,
        Func<TPage, IReadOnlyList<TItem>?> items,
        Func<TPage, int?> total)
    {
        const int pageSize = 100;
        var all = new List<TItem>();
        for (var offset = 0; offset < 100_000; offset += pageSize)
        {
            var page = await fetch(offset, pageSize).ConfigureAwait(false);
            var list = items(page);
            if (list is null || list.Count == 0) break;
            all.AddRange(list);
            if (total(page) is { } t)
            {
                if (offset + list.Count >= t) break;
            }
            else if (list.Count < pageSize)
            {
                break;
            }
        }
        return all;
    }
}
