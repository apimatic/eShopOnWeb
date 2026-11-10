using System;
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
using MindbodyPublicApi.Core.Models;
using MindbodyPublicApi.Core.ErrorResponse;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Owns the one Mindbody SDK client and the staff user token used for every call. The token is
/// cached until shortly before it expires and is re-issued on demand, so the process can run for
/// weeks. SDK failures are translated into <see cref="StudioException"/>s.
/// </summary>
public sealed class MindbodyGateway : IDisposable
{
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SiteInfoLifetime = TimeSpan.FromMinutes(30);

    private readonly MindbodySettings _settings;
    private readonly ILogger<MindbodyGateway> _logger;
    private readonly HttpClient? _httpClient;
    private readonly MindbodyPublicApiClient? _client;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly SemaphoreSlim _siteLock = new(1, 1);

    private volatile string? _token;
    private DateTimeOffset _tokenExpires;
    private volatile TimeZoneInfo? _siteZone;
    private DateTimeOffset _siteZoneLoaded;

    public MindbodyGateway(IOptions<MindbodySettings> settings, ILogger<MindbodyGateway> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (!IsConfigured)
            return;

        // Pooled connections are recycled regularly so DNS changes are picked up in a long-running process.
        _httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var options = new MindbodyPublicApiClientOptions
        {
            ApiKey = _settings.ApiKey,
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30), MaxRetries = 2 }
        };
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl;

        _client = new MindbodyPublicApiClient(_httpClient, options);
    }

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.ApiKey)
        && !string.IsNullOrWhiteSpace(_settings.SiteId)
        && !string.IsNullOrWhiteSpace(_settings.Username)
        && !string.IsNullOrWhiteSpace(_settings.Password);

    public string SiteId => _settings.SiteId ?? "";

    public MindbodyPublicApiClient Client =>
        _client ?? throw new StudioException(503, "Studio bookings are not available: the Mindbody integration is not configured.");

    /// <summary>
    /// Runs <paramref name="call"/> with a valid staff token. A rejected token is replaced once and the call retried.
    /// </summary>
    public async Task<T> CallAsync<T>(Func<string, Task<T>> call, CancellationToken cancellationToken = default)
    {
        _ = Client;
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(cancellationToken);
            try
            {
                return await call(token);
            }
            catch (ApiException ex) when (attempt == 0 && IsTokenRejection(ex))
            {
                _logger.LogInformation("Mindbody rejected the staff token; issuing a new one.");
                await InvalidateTokenAsync(token);
            }
            catch (Exception ex) when (ex is not StudioException and not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }
    }

    private static bool IsTokenRejection(ApiException ex)
    {
        if (ex.StatusCode == HttpStatusCode.Unauthorized)
            return true;
        if (ex is ApiException<RawError> raw)
        {
            var (_, code) = ReadError(raw.Error);
            return code != null && code.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        return false;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var current = _token;
        if (current != null && TokenValid())
            return current;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            current = _token;
            if (current != null && TokenValid())
                return current;

            IssueResponse response;
            try
            {
                response = await Client.UserToken.IssueToken(new IssueTokenRequest
                {
                    SiteId = _settings.SiteId!,
                    Body = new IssueRequest { Username = _settings.Username, Password = _settings.Password }
                }, null, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not obtain a Mindbody staff token.");
                throw ex is SdkTimeoutException or SdkConnectionException
                    ? new StudioException(503, "The studio system is not reachable right now. Please try again shortly.", ex)
                    : new StudioException(502, "The studio system did not accept the app's credentials.", ex);
            }

            if (string.IsNullOrEmpty(response.AccessToken))
                throw new StudioException(502, "The studio system did not issue an access token.");

            // Without an expiry, assume the token is short-lived.
            _tokenExpires = response.Expires ?? DateTimeOffset.UtcNow.AddHours(1);
            _token = response.AccessToken;
            return response.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool TokenValid() => DateTimeOffset.UtcNow < _tokenExpires - TokenRefreshMargin;

    private async Task InvalidateTokenAsync(string rejected)
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_token == rejected)
                _token = null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>The studio's time zone; Mindbody reports every date-time as local time of the site.</summary>
    public async Task<TimeZoneInfo> GetSiteZoneAsync(CancellationToken cancellationToken = default)
    {
        var zone = _siteZone;
        if (zone != null && DateTimeOffset.UtcNow - _siteZoneLoaded < SiteInfoLifetime)
            return zone;

        await _siteLock.WaitAsync(cancellationToken);
        try
        {
            zone = _siteZone;
            if (zone != null && DateTimeOffset.UtcNow - _siteZoneLoaded < SiteInfoLifetime)
                return zone;

            // The SDK's GetSites takes no SiteId, so the header is added per request; without it a
            // site-bound staff token is refused.
            var siteHeader = new RequestOptions { Headers = new[] { new HeaderParam("siteId", _settings.SiteId!) } };
            var response = await CallAsync(token => Client.SiteApi.GetSites(
                new GetSitesOperationRequest { Authorization = token }, siteHeader, cancellationToken), cancellationToken);

            Site? site = null;
            if (response.Sites is { Count: > 0 } sites)
            {
                foreach (var s in sites)
                {
                    if (s.Id?.ToString() == _settings.SiteId)
                    {
                        site = s;
                        break;
                    }
                }
                site ??= sites[0];
            }

            var zoneId = site?.TimeZone;
            if (string.IsNullOrWhiteSpace(zoneId))
                throw new StudioException(502, "The studio system did not report the studio's time zone.");

            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new StudioException(502, $"The studio's time zone '{zoneId}' is not known to this server.", ex);
            }
            _siteZone = zone;
            _siteZoneLoaded = DateTimeOffset.UtcNow;
            return zone;
        }
        finally
        {
            _siteLock.Release();
        }
    }

    internal static (string? Message, string? Code) ReadError(RawError error)
    {
        try
        {
            using var doc = JsonDocument.Parse(error.ReadAsBytes());
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Error", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                var message = e.TryGetProperty("Message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var code = e.TryGetProperty("Code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                return (message, code);
            }
        }
        catch (JsonException)
        {
        }
        return (null, null);
    }

    private StudioException Translate(Exception ex)
    {
        switch (ex)
        {
            case ApiException<RawError> api:
                var (message, code) = ReadError(api.Error);
                var reason = string.IsNullOrWhiteSpace(message)
                    ? $"The studio system refused the request ({(int)api.StatusCode})."
                    : message!;
                var status = (int)api.StatusCode;
                if (status == 404)
                    return new StudioException(404, reason, ex);
                if (status is 400 or 409 or 422)
                    return new StudioException(409, reason, ex);
                _logger.LogError(ex, "Mindbody call failed with {Status} {Code}: {Message}", status, code, message);
                return status == 429
                    ? new StudioException(503, "The studio system is busy. Please try again shortly.", ex)
                    : new StudioException(502, "The studio system could not process the request.", ex);
            case ResponseDeserializationException:
                _logger.LogWarning(ex, "A Mindbody answer could not be read.");
                return new MindbodyUnreadableResponseException(ex);
            case SdkTimeoutException:
            case SdkConnectionException:
                _logger.LogError(ex, "Mindbody is not reachable.");
                return new StudioException(503, "The studio system is not reachable right now. Please try again shortly.", ex);
            default:
                _logger.LogError(ex, "Unexpected Mindbody failure.");
                return new StudioException(502, "The studio system could not process the request.", ex);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _tokenLock.Dispose();
        _siteLock.Dispose();
    }
}
