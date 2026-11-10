using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Mindbody reports class and appointment times as wall-clock times of the studio's time zone, without an
/// offset (the SDK reads them as if they were UTC). This type puts the real offset back so that every time the
/// API returns is an ISO-8601 date-time with the correct UTC offset, daylight saving time included.
/// </summary>
public sealed class StudioClock
{
    public TimeZoneInfo Zone { get; }

    public StudioClock(TimeZoneInfo zone)
    {
        Zone = zone;
    }

    public static StudioClock FromId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new StudioException(System.Net.HttpStatusCode.BadGateway, "The studio's time zone is unknown.");
        }

        try
        {
            return new StudioClock(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId) && windowsId != null)
            {
                return new StudioClock(TimeZoneInfo.FindSystemTimeZoneById(windowsId));
            }
            throw new StudioException(System.Net.HttpStatusCode.BadGateway,
                $"The studio's time zone '{timeZoneId}' is not known to this server.");
        }
    }

    /// <summary>Studio-local wall clock of a value the SDK parsed from a Mindbody time.</summary>
    public static DateTime Wall(DateTimeOffset sdkValue) => DateTime.SpecifyKind(sdkValue.DateTime, DateTimeKind.Unspecified);

    /// <summary>Converts a studio wall-clock time to a real instant carrying the studio's UTC offset.</summary>
    public DateTimeOffset FromWall(DateTime wall)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(wall))
        {
            // A time skipped by the clocks going forward: the first valid moment after the gap.
            wall = wall.AddHours(1);
        }
        return new DateTimeOffset(wall, Zone.GetUtcOffset(wall));
    }

    public DateTimeOffset FromWall(DateTimeOffset sdkValue) => FromWall(Wall(sdkValue));

    /// <summary>Like <see cref="FromWall(DateTime)"/> but null for a wall-clock time that does not exist.</summary>
    public DateTimeOffset? TryFromWall(DateTime wall)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        return Zone.IsInvalidTime(wall) ? null : new DateTimeOffset(wall, Zone.GetUtcOffset(wall));
    }

    public DateTimeOffset StartOfDay(DateOnly day) => FromWall(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>The instant, expressed with the studio's offset at that instant.</summary>
    public DateTimeOffset ToStudio(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return utc.ToOffset(Zone.GetUtcOffset(utc));
    }

    public DateTimeOffset AddMinutes(DateTimeOffset start, int minutes) => ToStudio(start.AddMinutes(minutes));

    public DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(ToStudio(now).DateTime);
}
