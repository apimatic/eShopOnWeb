using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Mindbody reports class and session times as wall-clock times in the studio's time zone, without a UTC offset.
/// The SDK's JSON model reads such a value as a time in the server's own zone, so only its wall-clock part
/// (<see cref="DateTimeOffset.DateTime"/>) is meaningful. These helpers re-attach the studio's real offset.
/// </summary>
public static class StudioTime
{
    public static TimeZoneInfo? FindZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && windows != null)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(windows); } catch (Exception) { }
        }
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && iana != null)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(iana); } catch (Exception) { }
        }
        return null;
    }

    /// <summary>The wall-clock time of a Mindbody value as read by the SDK.</summary>
    public static DateTime Wall(DateTimeOffset value) => DateTime.SpecifyKind(value.DateTime, DateTimeKind.Unspecified);

    /// <summary>A studio wall-clock time with the studio's UTC offset at that moment.</summary>
    public static DateTimeOffset AtStudio(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1); // spring-forward gap
        var offset = zone.IsAmbiguousTime(wall) ? zone.BaseUtcOffset : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    /// <summary>A Mindbody wall-clock value (as read by the SDK) as a true instant shown with the studio's offset.</summary>
    public static DateTimeOffset FromMindbody(DateTimeOffset value, TimeZoneInfo zone) => AtStudio(Wall(value), zone);

    /// <summary>
    /// A request-side date/time. The SDK serialises request parameters as UTC and Mindbody converts them into the
    /// studio's zone, so the value must be the true instant of the studio-local time.
    /// </summary>
    public static DateTimeOffset QueryInstant(DateTime studioWall, TimeZoneInfo zone) => AtStudio(studioWall, zone);

    /// <summary>
    /// A request-body date/time. Mindbody reads the wall-clock digits of the serialised value as studio-local
    /// time and ignores the "Z", so the digits must be the studio-local time.
    /// </summary>
    public static DateTimeOffset BodyValue(DateTime studioWall) =>
        new(DateTime.SpecifyKind(studioWall, DateTimeKind.Unspecified), TimeSpan.Zero);

    public static DateTime StudioNow(TimeZoneInfo zone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime, DateTimeKind.Unspecified);
}
