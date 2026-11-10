using Microsoft.eShopWeb.PublicApi.Mindbody;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Book a place in a class. The client details are needed only with a shopper's first booking.</summary>
public class BookClassRequest : ClientDetails
{
    public int ClassId { get; set; }
}

/// <summary>Book a one-to-one session. The client details are needed only with a shopper's first booking.</summary>
public class BookAppointmentRequest : ClientDetails
{
    public int SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time, normally a startTime returned by /api/studio/slots.</summary>
    public string? StartTime { get; set; }

    /// <summary>Optional: restrict to one studio location.</summary>
    public int? LocationId { get; set; }

    /// <summary>Optional: ask for a specific trainer.</summary>
    public long? StaffId { get; set; }
}

public class AdminBookClassRequest
{
    /// <summary>Email address of the shopper (their eShop identity).</summary>
    public string? Shopper { get; set; }
    public int ClassId { get; set; }
}
