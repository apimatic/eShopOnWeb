using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.PublicApi.Mindbody;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Request bodies carry the new-client details inline (needed only for a shopper's first booking).</summary>
public class StudioClassBookingRequest : StudioClientDetails
{
    public long? ClassId { get; set; }
}

public class StudioAppointmentBookingRequest : StudioClientDetails
{
    public long? SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time with a UTC offset, e.g. 2026-11-12T11:00:00-08:00.</summary>
    public string? StartTime { get; set; }
    public long? StaffId { get; set; }
    public int? LocationId { get; set; }

    private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DateTimeOffset ParseStartTime()
    {
        if (string.IsNullOrWhiteSpace(StartTime)) throw StudioException.BadRequest("startTime is required.");
        if (!HasOffset.IsMatch(StartTime.Trim())
            || !DateTimeOffset.TryParse(StartTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw StudioException.BadRequest("startTime must be an ISO-8601 date-time with a UTC offset.");
        }
        return parsed;
    }
}

public class StudioAdminClassBookingRequest : StudioClientDetails
{
    /// <summary>The shopper's eShop user name or email.</summary>
    public string? Shopper { get; set; }
    public long? ClassId { get; set; }
}
