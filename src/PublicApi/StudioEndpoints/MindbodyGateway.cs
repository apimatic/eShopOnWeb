using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MindbodyPublicApi;
using MindbodyPublicApi.Core;
using MindbodyPublicApi.Core.ErrorResponse;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Core.Models;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Single point of contact with Mindbody: owns the SDK client, the staff user token (which expires and is
/// renewed for as long as the process lives) and the site's time zone.
/// </summary>
public class MindbodyGateway
{
    private static readonly TimeSpan TokenSafetyMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TimeZoneTtl = TimeSpan.FromHours(6);

    private readonly MindbodyOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly SemaphoreSlim _zoneLock = new(1, 1);

    private volatile string? _token;
    private DateTimeOffset _tokenExpires;
    private volatile TimeZoneInfo? _zone;
    private DateTimeOffset _zoneFetched;

    public MindbodyGateway(IOptions<MindbodyOptions> options, MindbodyPublicApiClient client)
    {
        _options = options.Value;
        Client = client;
    }

    public MindbodyPublicApiClient Client { get; }

    public string SiteId =>
        string.IsNullOrWhiteSpace(_options.SiteId) ? throw NotConfigured() : _options.SiteId;

    private static StudioException NotConfigured() =>
        new(503, "The studio integration is not configured (Mindbody settings are missing).");

    /// <summary>
    /// Runs a Mindbody call with a valid staff token; if Mindbody rejects the token it is renewed and the call repeated once.
    /// </summary>
    public async Task<T> CallAsync<T>(Func<string, Task<T>> call, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(null, cancellationToken);
        try
        {
            return await call(token);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The token was revoked or expired early: renew it and try once more.
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }

        token = await GetTokenAsync(token, cancellationToken);
        try
        {
            return await call(token);
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    private async Task<string> GetTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        if (rejectedToken is null && IsTokenFresh())
            return _token!;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have renewed the token while this one waited.
            if (IsTokenFresh() && _token != rejectedToken)
                return _token!;

            if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SiteId)
                || string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
                throw NotConfigured();

            IssueResponse response;
            try
            {
                response = await Client.UserToken.IssueToken(new IssueTokenRequest
                {
                    SiteId = _options.SiteId,
                    Body = new IssueRequest { Username = _options.Username, Password = _options.Password }
                }, null, cancellationToken);
            }
            catch (Exception ex)
            {
                throw Translate(ex);
            }

            if (string.IsNullOrEmpty(response.AccessToken))
                throw new StudioException(502, "The studio system did not issue an access token.");

            _tokenExpires = response.Expires ?? DateTimeOffset.UtcNow.AddHours(1);
            _token = response.AccessToken;
            return response.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool IsTokenFresh() =>
        _token is not null && DateTimeOffset.UtcNow + TokenSafetyMargin < _tokenExpires;

    /// <summary>
    /// The studio's time zone, as configured in Mindbody.
    /// </summary>
    public async Task<TimeZoneInfo> GetSiteTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        var cached = _zone;
        if (cached is not null && DateTimeOffset.UtcNow - _zoneFetched < TimeZoneTtl)
            return cached;

        await _zoneLock.WaitAsync(cancellationToken);
        try
        {
            cached = _zone;
            if (cached is not null && DateTimeOffset.UtcNow - _zoneFetched < TimeZoneTtl)
                return cached;

            var siteId = SiteId;
            var response = await CallAsync(token => Client.SiteApi.GetSites(new GetSitesOperationRequest
            {
                Authorization = token,
                RequestSiteIds = int.TryParse(siteId, out var id) ? new[] { id } : null
            }, new RequestOptions { Headers = new[] { new HeaderParam("SiteId", siteId) } }, cancellationToken), cancellationToken);

            var zoneName = response.Sites is { Count: > 0 } ? response.Sites[0].TimeZone : null;
            if (string.IsNullOrWhiteSpace(zoneName))
                throw new StudioException(502, "The studio system did not report the studio's time zone.");

            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(zoneName);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new StudioException(502, $"The studio's time zone '{zoneName}' is not known to this server.");
            }

            _zoneFetched = DateTimeOffset.UtcNow;
            _zone = zone;
            return zone;
        }
        finally
        {
            _zoneLock.Release();
        }
    }

    /// <summary>
    /// Mindbody reports date-times as the studio's wall-clock time without an offset. This attaches the studio's
    /// real UTC offset for that moment.
    /// </summary>
    public static DateTimeOffset ToSiteTime(DateTimeOffset reported, TimeZoneInfo zone) =>
        FromWallClock(reported.DateTime, zone);

    public static DateTimeOffset FromWallClock(DateTime wallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    /// <summary>
    /// Mindbody takes the studio's wall-clock time in date-time parameters; the SDK writes these as UTC, so the
    /// wall-clock value is carried at offset zero.
    /// </summary>
    public static DateTimeOffset ToMindbodyWallClock(DateTime wallClock) =>
        new(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), TimeSpan.Zero);

    public static StudioException Translate(Exception ex)
    {
        switch (ex)
        {
            case StudioException studio:
                return studio;
            case ResponseDeserializationException:
                return new StudioResponseUnreadableException();
            case ApiException<RawError> api:
                return TranslateApi(api, api.Error);
            case ApiException:
                return new StudioException(502, "The studio system returned an unexpected error.");

            case OperationCanceledException:
            case SdkTimeoutException:
                return new StudioException(504, "The studio system did not answer in time.");
            case SdkConnectionException:
            case HttpRequestException:
                return new StudioException(503, "The studio system could not be reached.");
            default:
                return new StudioException(502, "The studio system returned an unexpected response.");
        }
    }

    private static StudioException TranslateApi(ApiException api, RawError error)
    {
        var message = ExtractMessage(error);
        var status = (int)api.StatusCode;
        return status switch
        {
            400 or 409 or 422 => new StudioException(409, message ?? "The studio system could not carry out the request."),
            404 => new StudioException(404, message ?? "Not found in the studio system."),
            401 or 403 => new StudioException(502, "The studio system rejected this integration's credentials."),
            429 => new StudioException(503, "The studio system is busy; try again shortly."),
            >= 500 => new StudioException(503, "The studio system is temporarily unavailable."),
            _ => new StudioException(502, message ?? "The studio system returned an unexpected error.")
        };
    }

    private static string? ExtractMessage(RawError error)
    {
        try
        {
            using var doc = JsonDocument.Parse(error.ReadAsBytes());
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Error", out var e)
                && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("Message", out var m)
                && m.ValueKind == JsonValueKind.String)
                return m.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
