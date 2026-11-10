using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>A class shoppers can attend. Times are ISO-8601 with the studio's UTC offset.</summary>
public record StudioClassDto(
    long Id,
    string Name,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int? LocationId,
    string? LocationName,
    string? InstructorName);

public record StudioSlotDto(DateTimeOffset StartTime, DateTimeOffset EndTime);

/// <summary>A trainer with every contact detail Mindbody holds (staff use only).</summary>
public record StudioTrainerDto(
    long Id,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    bool Active,
    bool ClassTeacher,
    bool AppointmentInstructor,
    string? Email,
    string? MobilePhone,
    string? HomePhone,
    string? WorkPhone,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Bio,
    string? ImageUrl);

public record StudioBookingDto(
    string BookingId,
    string Kind,
    long? ClassId,
    long? SessionTypeId,
    long? StaffId,
    int? LocationId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string ClientId,
    bool AlreadyBooked);

public record StudioCancellationDto(string BookingId, string Status);

/// <summary>Who the studio client is, taken from the eShop identity.</summary>
public record StudioShopper(string Email, string? FirstName = null, string? LastName = null);

/// <summary>
/// The details the studio asks of every new client; required with a shopper's first booking.
/// </summary>
public class StudioClientDetails
{
    public int? HomeLocationId { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }

    // Accepted aliases for the same details.
    public string? StreetAddress { get => AddressLine1; set => AddressLine1 ??= value; }
    public DateOnly? DateOfBirth { get => BirthDate; set => BirthDate ??= value; }
    public string? MobileNumber { get => MobilePhone; set => MobilePhone ??= value; }
    public string? PostalZip { get => PostalCode; set => PostalCode ??= value; }

    /// <summary>Optional: the eShop identity carries no name, so these may be supplied.</summary>
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public record StudioClientRef(string Id, int? HomeLocationId);

public record StudioTrainerList(IReadOnlyList<StudioTrainerDto> Trainers);
