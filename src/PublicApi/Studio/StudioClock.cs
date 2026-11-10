using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MindbodyPublicApi.Core;
using MindbodyPublicApi.Core.Models;
using MindbodyPublicApi.Requests.SiteApi;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Mindbody reports every schedule time as local wall-clock time of the studio, with no offset.
/// This turns those wall-clock times into real instants and back into date-times that carry the
/// studio's UTC offset <em>of that day</em> (the offset changes with daylight saving time, so it is
/// never cached as a fixed number).
/// </summary>
public sealed class StudioClock
{
    private static readonly TimeSpan ZoneCacheLifetime = TimeSpan.FromHours(6);

    private readonly MindbodyGateway _gateway;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TimeZoneInfo? _zone;
    private DateTimeOffset _zoneLoadedAt;

    public StudioClock(MindbodyGateway gateway)
    {
        _gateway = gateway;
    }

    public DateTimeOffset UtcNow => _gateway.Time.GetUtcNow();

    public async Task<TimeZoneInfo> GetZoneAsync(CancellationToken cancellationToken)
    {
        var zone = _zone;
        if (zone is not null && UtcNow - _zoneLoadedAt < ZoneCacheLifetime)
        {
            return zone;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_zone is not null && UtcNow - _zoneLoadedAt < ZoneCacheLifetime)
            {
                return _zone;
            }

            try
            {
                var siteId = int.Parse(_gateway.SiteId, CultureInfo.InvariantCulture);
                // This operation has no site header of its own, and Mindbody rejects a staff token that is not
                // tied to the site it is asked about, so the site is added as an extra header.
                var siteHeader = new RequestOptions { Headers = [new HeaderParam("siteId", _gateway.SiteId)] };
                var sites = await _gateway.CallAsync((client, token) => client.SiteApi.GetSites(new GetSitesOperationRequest
                {
                    RequestSiteIds = [siteId],
                    Authorization = token,
                }, siteHeader, cancellationToken), cancellationToken).ConfigureAwait(false);

                var id = sites.Sites?.Count > 0 ? sites.Sites[0].TimeZone : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new StudioException(502, "The studio system did not report the studio's time zone.");
                }

                _zone = FindZone(id);
                _zoneLoadedAt = UtcNow;
            }
            catch (StudioException) when (_zone is not null)
            {
                // The zone of a studio practically never changes: keep serving the last known one
                // rather than failing every request while Mindbody has a bad moment.
                _zoneLoadedAt = UtcNow - ZoneCacheLifetime + TimeSpan.FromMinutes(1);
            }

            return _zone!;
        }
        finally
        {
            _lock.Release();
        }
    }

    public static TimeZoneInfo FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && windowsId is not null)
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }

            throw new StudioException(502, $"The studio's time zone '{id}' is not known to this server.", ex);
        }
    }

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var zone = await GetZoneAsync(cancellationToken).ConfigureAwait(false);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, zone).DateTime);
    }

    /// <summary>The instant a studio wall-clock time (as reported by Mindbody) happens.</summary>
    public static DateTimeOffset ToInstant(DateTime wallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            // Wall-clock time skipped by a spring-forward change: it happens once the clocks have moved on.
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    /// <summary>The same instant, expressed with the studio's UTC offset at that instant.</summary>
    public static DateTimeOffset ToStudioTime(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone);

    /// <summary>
    /// Converts a time the SDK deserialised from Mindbody. The SDK reads offset-less times as UTC, but they
    /// are really studio wall-clock times, so only the clock reading is meaningful.
    /// </summary>
    public static DateTimeOffset FromMindbody(DateTimeOffset reportedByMindbody, TimeZoneInfo zone) =>
        ToStudioTime(ToInstant(reportedByMindbody.DateTime, zone), zone);

    /// <summary>Midnight (studio time) at the start of a studio calendar day, as an instant.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone) =>
        ToInstant(day.ToDateTime(TimeOnly.MinValue), zone);
}
