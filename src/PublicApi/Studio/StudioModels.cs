using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>The eShop identity of a shopper, as far as the studio is concerned.</summary>
public record StudioShopper(string Email, string UserName);

/// <summary>What the studio asks of every new client; only needed with a shopper's first booking.</summary>
public class NewClientDetails
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int? HomeLocationId { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }
}

public class BookClassRequest : NewClientDetails
{
    public int? ClassId { get; set; }
}

public class BookAppointmentRequest : NewClientDetails
{
    public int? SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time with UTC offset, as returned by the slots endpoint.</summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>Optional: restrict to a location.</summary>
    public int? LocationId { get; set; }

    /// <summary>Optional: restrict to a trainer.</summary>
    public long? StaffId { get; set; }
}

public class AdminBookClassRequest
{
    /// <summary>The shopper's eShop e-mail address (or user name).</summary>
    public string? Shopper { get; set; }
    public int? ClassId { get; set; }
}

public record ClassDto(long Id, string Name, string? Description, string StartTime, string EndTime,
    int? LocationId, string? LocationName, string? Trainer, int? SpotsLeft);

public record SlotDto(string StartTime, string EndTime);

public record ClassBookingDto(long BookingId, long ClassId, string Name, string StartTime, string EndTime, int? LocationId);

public record AppointmentBookingDto(long BookingId, int SessionTypeId, string Name, string StartTime, string EndTime,
    int? LocationId, long? TrainerId);

public record CancelledBookingDto(long BookingId, string Status);

public record TrainerDto(long Id, string Name, string? FirstName, string? LastName, bool Active,
    bool TeachesClasses, bool TakesAppointments,
    string? Email, string? MobilePhone, string? HomePhone, string? WorkPhone,
    string? Address, string? City, string? State, string? PostalCode, string? Country);
