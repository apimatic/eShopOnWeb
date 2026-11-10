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
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The only place that talks to Mindbody through the SDK. It owns the staff user token (issued on demand,
/// renewed before it expires, re-issued when Mindbody rejects it), the site's time zone, and the translation
/// of Mindbody/SDK failures into <see cref="StudioException"/>s. It is a singleton and is safe for the
/// application to keep using for weeks.
/// </summary>
public sealed class MindbodyGateway : IDisposable
{
    private static readonly TimeSpan TokenRenewMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan ZoneCacheLifetime = TimeSpan.FromHours(6);

    // Keep each Mindbody call comfortably inside the time the mobile app is willing to wait.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly MindbodySettings _settings;
    private readonly ILogger<MindbodyGateway> _logger;
    private readonly TimeProvider _time;
    private readonly object _clientInit = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly SemaphoreSlim _zoneLock = new(1, 1);

    private HttpClient? _http;
    private MindbodyPublicApiClient? _client;
    private string? _token;
    private DateTimeOffset _tokenExpiry;
    private TimeZoneInfo? _zone;
    private DateTimeOffset _zoneFetched;

    public MindbodyGateway(IOptions<MindbodySettings> settings, ILogger<MindbodyGateway> logger, TimeProvider? time = null)
    {
        var s = settings.Value;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        // Fall back to the documented sandbox environment variables when the Mindbody: keys are not set.
        _settings = new MindbodySettings
        {
            ApiKey = FirstNonEmpty(s.ApiKey, Environment.GetEnvironmentVariable("MINDBODY_API_KEY")),
            SiteId = FirstNonEmpty(s.SiteId, Environment.GetEnvironmentVariable("MINDBODY_SITE_ID")),
            Username = FirstNonEmpty(s.Username, Environment.GetEnvironmentVariable("MINDBODY_USERNAME")),
            Password = FirstNonEmpty(s.Password, Environment.GetEnvironmentVariable("MINDBODY_PASSWORD")),
            BaseUrl = FirstNonEmpty(s.BaseUrl),
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }

    public DateTimeOffset UtcNow => _time.GetUtcNow();

    // ---------------------------------------------------------------- client & token

    private (MindbodyPublicApiClient Client, string SiteId) GetClient()
    {
        if (_client is not null) return (_client, _settings.SiteId!);

        lock (_clientInit)
        {
            if (_client is not null) return (_client, _settings.SiteId!);

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_settings.ApiKey)) missing.Add("Mindbody:ApiKey");
            if (string.IsNullOrWhiteSpace(_settings.SiteId)) missing.Add("Mindbody:SiteId");
            if (string.IsNullOrWhiteSpace(_settings.Username)) missing.Add("Mindbody:Username");
            if (string.IsNullOrWhiteSpace(_settings.Password)) missing.Add("Mindbody:Password");
            if (missing.Count > 0)
            {
                throw new StudioException(HttpStatusCode.ServiceUnavailable,
                    $"The studio integration is not configured (missing {string.Join(", ", missing)}).");
            }

            // Connections are recycled so DNS changes are picked up by an app that never restarts.
            var handler = new SiteHeaderHandler(_settings.SiteId!)
            {
                InnerHandler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }
            };
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

            var options = new MindbodyPublicApiClientOptions
            {
                ApiKey = _settings.ApiKey,
                TimeProvider = _time,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Delay = TimeSpan.FromMilliseconds(300),
                    Timeout = RequestTimeout,
                }
            };
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = _settings.BaseUrl!;
            }

            _client = new MindbodyPublicApiClient(_http, options);
            return (_client, _settings.SiteId!);
        }
    }

    private async Task<string> GetTokenAsync(string? rejected, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var current = _token;
        if (current is not null && current != rejected && now < _tokenExpiry - TokenRenewMargin) return current;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = _time.GetUtcNow();
            if (_token is not null && _token != rejected && now < _tokenExpiry - TokenRenewMargin) return _token;

            var (client, siteId) = GetClient();
            IssueResponse issued;
            try
            {
                issued = await client.UserToken.IssueToken(new IssueTokenRequest
                {
                    SiteId = siteId,
                    Body = new IssueRequest { Username = _settings.Username, Password = _settings.Password }
                }, null, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not StudioException && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
            {
                _token = null;
                _logger.LogError(ex, "Could not obtain a Mindbody staff user token");
                throw Translate(ex, "obtaining the studio access token");
            }

            if (string.IsNullOrEmpty(issued.AccessToken))
            {
                throw new StudioException(HttpStatusCode.BadGateway, "Mindbody did not issue an access token.");
            }

            _token = issued.AccessToken;
            _tokenExpiry = issued.Expires ?? now + DefaultTokenLifetime;
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void Invalidate(string rejected)
    {
        if (_token == rejected) _token = null;
    }

    /// <summary>
    /// Runs a Mindbody call with a valid staff token. A rejected token is replaced once and the call repeated.
    /// </summary>
    /// <param name="tolerateUnreadableBody">
    /// Some Mindbody operations succeed but answer with a body the SDK cannot read; when true that outcome is
    /// reported as <c>default</c> instead of a failure and the caller must verify the effect itself.
    /// </param>
    public async Task<T?> CallAsync<T>(Func<MindbodyPublicApiClient, string, string, Task<T>> call,
        string what, CancellationToken ct = default, bool tolerateUnreadableBody = false)
    {
        string? rejected = null;
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(rejected, ct).ConfigureAwait(false);
            var (client, siteId) = GetClient();
            try
            {
                return await call(client, siteId, token).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _logger.LogWarning("Mindbody rejected the staff token; issuing a new one");
                Invalidate(token);
                rejected = token;
            }
            catch (ResponseDeserializationException) when (tolerateUnreadableBody)
            {
                return default;
            }
            catch (Exception ex) when (ex is not StudioException && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
            {
                throw Translate(ex, what);
            }
        }
    }

    // ---------------------------------------------------------------- errors

    private StudioException Translate(Exception ex, string what)
    {
        switch (ex)
        {
            case StudioException se:
                return se;
            case ApiException api:
            {
                var reason = ReadReason(api);
                var status = (int)api.StatusCode;
                if (status == 401 || status == 403)
                {
                    _logger.LogError("Mindbody refused access while {What}: {Status} {Reason}", what, status, reason);
                    return new StudioException(HttpStatusCode.BadGateway,
                        $"The studio system refused the integration's credentials while {what}.", ex);
                }
                if (status == 429)
                {
                    return new StudioException(HttpStatusCode.ServiceUnavailable,
                        "The studio system is busy; please try again shortly.", ex);
                }
                if (status >= 400 && status < 500)
                {
                    var message = string.IsNullOrWhiteSpace(reason)
                        ? $"The studio system could not carry out the request ({what})."
                        : $"The studio system could not carry out the request ({what}): {reason}";
                    return new StudioException(status == 404 ? HttpStatusCode.NotFound : HttpStatusCode.UnprocessableEntity, message, ex);
                }
                _logger.LogError(ex, "Mindbody failed while {What}: {Status}", what, status);
                return new StudioException(HttpStatusCode.BadGateway, $"The studio system failed while {what}.", ex);
            }
            case SdkTimeoutException:
            case OperationCanceledException:
                return new StudioException(HttpStatusCode.GatewayTimeout, $"The studio system did not answer in time while {what}.", ex);
            default:
                _logger.LogError(ex, "Unexpected failure while {What}", what);
                return new StudioException(HttpStatusCode.BadGateway, $"The studio system could not be reached while {what}.", ex);
        }
    }

    private static string? ReadReason(ApiException api)
    {
        if (api is not ApiException<RawError> raw) return null;
        try
        {
            var text = raw.Error.ReadAsString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("Error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("Message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
            return text.Length > 300 ? text[..300] : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- site time zone

    /// <summary>
    /// Mindbody reports every date and time as the site's wall-clock time with no offset, and the SDK parses
    /// such text using the machine's own zone. The site's real zone is needed to produce correct offsets.
    /// </summary>
    public async Task<TimeZoneInfo> GetSiteZoneAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        if (_zone is not null && now - _zoneFetched < ZoneCacheLifetime) return _zone;

        await _zoneLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = _time.GetUtcNow();
            if (_zone is not null && now - _zoneFetched < ZoneCacheLifetime) return _zone;

            try
            {
                var response = await CallAsync((c, site, token) => c.SiteApi.GetSites(new GetSitesOperationRequest
                {
                    Authorization = token,
                    RequestSiteIds = int.TryParse(site, out var id) ? new[] { id } : null,
                }, null, ct), "reading the studio's time zone", ct).ConfigureAwait(false);

                var zoneId = response?.Sites?.Count > 0 ? response.Sites[0].TimeZone : null;
                if (string.IsNullOrWhiteSpace(zoneId))
                {
                    throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not report its time zone.");
                }

                try
                {
                    _zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    throw new StudioException(HttpStatusCode.BadGateway, $"The studio's time zone '{zoneId}' is not known to this server.", ex);
                }
                _zoneFetched = now;
            }
            catch (StudioException) when (_zone is not null)
            {
                // Keep serving with the last known zone if a refresh fails; retry in five minutes.
                _logger.LogWarning("Could not refresh the studio time zone; using the previous value");
                _zoneFetched = now - ZoneCacheLifetime + TimeSpan.FromMinutes(5);
            }
            return _zone!;
        }
        finally
        {
            _zoneLock.Release();
        }
    }

    // ---------------------------------------------------------------- paging

    public const int PageSize = 100;

    /// <summary>Reads every page of a Mindbody list (they default to 100 items and silently truncate).</summary>
    public static async Task<List<T>> ReadAllAsync<T>(Func<int, int, Task<(IReadOnlyList<T>? Items, int? Total)>> fetchPage)
    {
        var all = new List<T>();
        for (var page = 0; page < 1000; page++)
        {
            var (items, total) = await fetchPage(all.Count, PageSize).ConfigureAwait(false);
            var count = items?.Count ?? 0;
            if (count == 0) break;
            all.AddRange(items!);
            if (total is int t ? all.Count >= t : count < PageSize) break;
        }
        return all;
    }

    // ---------------------------------------------------------------- time helpers

    /// <summary>
    /// Re-reads a time the SDK parsed from Mindbody's offset-less site-local text: the wall-clock digits are
    /// authoritative; the offset the SDK attached (that of the machine) is discarded.
    /// </summary>
    public static DateTimeOffset FromSiteWall(DateTimeOffset parsed, TimeZoneInfo zone) => FromSiteWall(parsed.DateTime, zone);

    public static DateTimeOffset FromSiteWall(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
        return new DateTimeOffset(wall, zone.GetUtcOffset(wall));
    }

    /// <summary>
    /// Mindbody takes the wall-clock digits of a request time as site-local, ignoring the zone, so a site-local
    /// time must be sent with a zero offset to arrive unchanged.
    /// </summary>
    public static DateTimeOffset ToSiteWallRequest(DateTime siteWall) =>
        new(DateTime.SpecifyKind(siteWall, DateTimeKind.Unspecified), TimeSpan.Zero);

    /// <summary>
    /// Mindbody treats the end of a date range as an instant that is exclusive, so to include the whole of
    /// the last day the range must end at the very end of that day.
    /// </summary>
    public static DateTimeOffset ToSiteWallRequestEnd(DateOnly siteDate) =>
        ToSiteWallRequest(siteDate.ToDateTime(new TimeOnly(23, 59, 59)));

    public static DateTimeOffset ToSiteWallRequest(DateOnly siteDate) => ToSiteWallRequest(siteDate.ToDateTime(TimeOnly.MinValue));

    public DateOnly SiteToday(TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone).DateTime);

    public void Dispose()
    {
        _http?.Dispose();
        _tokenLock.Dispose();
        _zoneLock.Dispose();
    }

    private sealed class SiteHeaderHandler(string siteId) : DelegatingHandler
    {
        // The SDK's GetSites operation does not send the SiteId header that Mindbody requires; add it.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.Contains("SiteId"))
            {
                request.Headers.TryAddWithoutValidation("SiteId", siteId);
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
