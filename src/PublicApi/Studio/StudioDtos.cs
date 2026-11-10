using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>Details the studio asks of every new client; required with a shopper's first booking.</summary>
public class NewClientDetails
{
    public int? HomeLocationId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }
}

public class ClassBookingRequest : NewClientDetails
{
    public long? ClassId { get; set; }
}

public class AppointmentBookingRequest : NewClientDetails
{
    public int? SessionTypeId { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public long? StaffId { get; set; }
    public int? LocationId { get; set; }
}

public class AdminClassBookingRequest
{
    /// <summary>The shopper's eShop identity (their sign-in e-mail) or their studio client id.</summary>
    public string? Shopper { get; set; }
    public long? ClassId { get; set; }
}

public record ClassDto(long Id, string? Name, DateTimeOffset StartTime, DateTimeOffset EndTime,
    int? LocationId, string? LocationName, string? Trainer, int? SpotsLeft);

public record SlotDto(DateTimeOffset StartTime, DateTimeOffset EndTime);

public record ClassBookingDto(long BookingId, long ClassId, string? Name, DateTimeOffset StartTime, DateTimeOffset EndTime);

public record AppointmentBookingDto(long BookingId, int SessionTypeId, long StaffId, int LocationId,
    DateTimeOffset StartTime, DateTimeOffset EndTime);

public record CancelledDto(long BookingId, string Status);

public record TrainerDto(long Id, bool Active, string? FirstName, string? LastName, string? DisplayName,
    string? Email, string? HomePhone, string? MobilePhone, string? WorkPhone,
    string? Address, string? City, string? State, string? PostalCode, string? Country);
