using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Details the studio asks of every new client. Only needed with a shopper's first booking.
/// </summary>
public class ClientRegistration
{
    public int? HomeLocationId { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class BookClassRequest : ClientRegistration
{
    public long? ClassId { get; set; }
}

public class BookAppointmentRequest : ClientRegistration
{
    public int? SessionTypeId { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public int? StaffId { get; set; }
    public int? LocationId { get; set; }
}

public class AdminBookClassRequest
{
    /// <summary>The shopper's eShop user name (their e-mail address).</summary>
    public string? Shopper { get; set; }
    public long? ClassId { get; set; }
}

public record StudioClassDto(
    long Id,
    string? Name,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int? LocationId,
    string? LocationName,
    string? Instructor,
    int? SpotsAvailable);

public record SlotDto(DateTimeOffset StartTime, DateTimeOffset EndTime);

public record ClassBookingDto(long BookingId, long ClassId, string? Name, DateTimeOffset StartTime, DateTimeOffset EndTime);

public record AppointmentBookingDto(
    long BookingId,
    int SessionTypeId,
    string? Name,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int? StaffId,
    int? LocationId);

public record TrainerDto(
    long Id,
    string? Name,
    string? FirstName,
    string? LastName,
    bool Active,
    bool TeachesClasses,
    bool TakesAppointments,
    string? Email,
    string? MobilePhone,
    string? HomePhone,
    string? WorkPhone,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);
