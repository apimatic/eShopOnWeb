using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindbodyPublicApi;
using MindbodyPublicApi.Core;
using MindbodyPublicApi.Core.Configuration;
using MindbodyPublicApi.Core.ErrorResponse;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Core.Models;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.UserToken;
using Mb = MindbodyPublicApi.Models;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Owns the one long-lived Mindbody SDK client, the staff user token (renewed before it expires so the
/// process can run for weeks) and the studio's time zone. All calls to Mindbody go through <see cref="CallAsync{T}"/>.
/// </summary>
public sealed class MindbodyGateway : IDisposable
{
    private static readonly TimeSpan TokenRenewMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SiteInfoLifetime = TimeSpan.FromHours(6);

    private readonly MindbodySettings _settings;
    private readonly ILogger<MindbodyGateway> _logger;
    private readonly Lazy<(HttpClient Http, MindbodyPublicApiClient Client)> _client;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiry;

    private readonly SemaphoreSlim _siteLock = new(1, 1);
    private TimeZoneInfo? _timeZone;
    private DateTimeOffset _timeZoneFetched;

    public MindbodyGateway(IOptions<MindbodySettings> settings, ILogger<MindbodyGateway> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _client = new Lazy<(HttpClient, MindbodyPublicApiClient)>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private (HttpClient, MindbodyPublicApiClient) CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.SiteId) ||
            string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
        {
            throw new StudioException(503, "The studio booking system is not configured (Mindbody settings are missing).");
        }

        var options = new MindbodyPublicApiClientOptions
        {
            ApiKey = _settings.ApiKey,
            // Fail within a reasonable time so the mobile app gets an answer; the SDK retries transient failures of idempotent calls.
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(20) },
        };
        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            // Used verbatim as the API's base address.
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl;
        }

        // Connections are recycled periodically so DNS changes are honoured in a long-running process.
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return (http, new MindbodyPublicApiClient(http, options));
    }

    public string SiteId => _settings.SiteId!;

    private MindbodyPublicApiClient Client => _client.Value.Client;

    /// <summary>
    /// Runs a Mindbody call with a valid staff token. A rejected token (expired/revoked) is discarded and
    /// the call is retried once with a fresh one. SDK failures are translated to <see cref="StudioException"/>.
    /// For calls that change state, <paramref name="passUnreadableResponse"/> lets a response the SDK could not parse
    /// (<see cref="ResponseDeserializationException"/>, raised after Mindbody answered 200) reach the caller, who
    /// must then verify what actually happened instead of reporting a failure.
    /// </summary>
    public async Task<T> CallAsync<T>(Func<MindbodyPublicApiClient, string, string, Task<T>> call, CancellationToken ct, bool passUnreadableResponse = false)
    {
        var client = Client;
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(ct).ConfigureAwait(false);
            try
            {
                return await call(client, SiteId, token).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _logger.LogInformation("Mindbody rejected the staff token; issuing a new one.");
                await InvalidateTokenAsync(token).ConfigureAwait(false);
            }
            catch (ResponseDeserializationException) when (passUnreadableResponse)
            {
                throw;
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is { } t && DateTimeOffset.UtcNow < _tokenExpiry - TokenRenewMargin) return t;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is { } t2 && DateTimeOffset.UtcNow < _tokenExpiry - TokenRenewMargin) return t2;

            try
            {
                var response = await Client.UserToken.IssueToken(new IssueTokenRequest
                {
                    SiteId = SiteId,
                    Body = new Mb.IssueRequest { Username = _settings.Username, Password = _settings.Password },
                }, null, ct).ConfigureAwait(false);

                if (string.IsNullOrEmpty(response.AccessToken))
                    throw new StudioException(503, "The studio booking system did not issue an access token.");

                _token = response.AccessToken;
                _tokenExpiry = response.Expires ?? DateTimeOffset.UtcNow.AddMinutes(30);
                return _token;
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task InvalidateTokenAsync(string rejected)
    {
        await _tokenLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_token == rejected) _token = null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>The studio's time zone, from the site settings. Mindbody reports times as wall-clock times in this zone.</summary>
    public async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken ct)
    {
        if (_timeZone is { } tz && DateTimeOffset.UtcNow - _timeZoneFetched < SiteInfoLifetime) return tz;

        await _siteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_timeZone is { } tz2 && DateTimeOffset.UtcNow - _timeZoneFetched < SiteInfoLifetime) return tz2;
            try
            {
                var sites = await CallAsync((c, site, token) => c.SiteApi.GetSites(
                    new GetSitesOperationRequest { Authorization = token },
                    new RequestOptions { Headers = [new HeaderParam("siteId", site)] }, ct), ct).ConfigureAwait(false);

                var name = sites.Sites?.FirstOrDefault(s => s.Id?.ToString() == SiteId)?.TimeZone
                           ?? sites.Sites?.FirstOrDefault()?.TimeZone;
                _timeZone = StudioTime.FindZone(name) ?? throw new StudioException(503, "The studio's time zone is unknown.");
                _timeZoneFetched = DateTimeOffset.UtcNow;
            }
            catch (StudioException) when (_timeZone != null)
            {
                // Keep serving with the previously known zone if a refresh fails; try again in a few minutes.
                _timeZoneFetched = DateTimeOffset.UtcNow - SiteInfoLifetime + TimeSpan.FromMinutes(5);
            }
            return _timeZone!;
        }
        finally
        {
            _siteLock.Release();
        }
    }

    public static StudioException Translate(Exception ex)
    {
        switch (ex)
        {
            case ApiException api:
                var message = ExtractMessage(api) ?? $"The studio system rejected the request ({(int)api.StatusCode}).";
                var status = (int)api.StatusCode;
                if (status == 404) return new StudioException(404, message, ex);
                if (status is 429 or >= 500) return new StudioException(503, "The studio system is busy or unavailable. Please try again. " + message, ex);
                if (status is 401 or 403) return new StudioException(503, "The studio system refused this app's credentials. " + message, ex);
                return new StudioException(409, message, ex);
            case SdkTimeoutException:
                return new StudioException(504, "The studio system did not answer in time. Please try again.", ex);
            case SdkConnectionException:
                return new StudioException(503, "The studio system could not be reached. Please try again.", ex);
            case AuthSchemeException:
                return new StudioException(503, "The studio booking system is not configured (Mindbody API key is missing).", ex);
            case HttpRequestException:
                return new StudioException(503, "The studio system could not be reached. Please try again.", ex);
            default:
                return new StudioException(502, "The studio system returned an unexpected response.", ex);
        }
    }

    private static string? ExtractMessage(ApiException api)
    {
        try
        {
            if (api is ApiException<RawError> raw)
            {
                using var doc = JsonDocument.Parse(raw.Error.ReadAsBytes());
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Error", out var e) && e.ValueKind == JsonValueKind.Object &&
                    e.TryGetProperty("Message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    return m.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    public void Dispose()
    {
        if (_client.IsValueCreated) _client.Value.Http.Dispose();
        _tokenLock.Dispose();
        _siteLock.Dispose();
    }
}
