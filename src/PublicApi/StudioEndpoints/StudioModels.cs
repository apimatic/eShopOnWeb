using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// What the studio asks of every new client; only needed with a shopper's first booking.
/// </summary>
public class NewClientDetails
{
    public int? HomeLocationId { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Date of birth, as yyyy-MM-dd.</summary>
    public string? DateOfBirth { get; set; }
    public string? MobilePhone { get; set; }
    /// <summary>How the shopper heard of the studio (one of the studio's referral types).</summary>
    public string? ReferredBy { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class ClassBookingRequest : NewClientDetails
{
    public int? ClassId { get; set; }
}

public class AppointmentBookingRequest : NewClientDetails
{
    public int? SessionTypeId { get; set; }
    /// <summary>Start of the session, an ISO-8601 date-time (with UTC offset).</summary>
    public string? StartTime { get; set; }
    public long? StaffId { get; set; }
    public int? LocationId { get; set; }
}

public class AdminClassBookingRequest : NewClientDetails
{
    /// <summary>The shopper's eShop user name (their e-mail address).</summary>
    public string? Shopper { get; set; }
    public int? ClassId { get; set; }
}

public record StudioLocationDto(int? Id, string? Name);

public record StudioClassDto(
    int ClassId,
    string? Name,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    StudioLocationDto? Location,
    string? Trainer,
    int? SpotsLeft,
    bool IsWaitlistAvailable);

public record StudioClassListResponse(DateOnly From, DateOnly To, IReadOnlyList<StudioClassDto> Classes);

public record StudioSlotDto(DateTimeOffset StartTime, DateTimeOffset EndTime);

public record StudioSlotListResponse(
    int SessionTypeId,
    string? SessionTypeName,
    int DurationMinutes,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<StudioSlotDto> Slots);

public record StudioBookingDto(
    long BookingId,
    string Kind,
    string? Name,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int? LocationId,
    long? StaffId,
    string Status);

public record StudioCancellationDto(long BookingId, string Kind, string Status);

public record StudioTrainerDto(
    long Id,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? Email,
    string? MobilePhone,
    string? HomePhone,
    string? WorkPhone,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    bool TeachesClasses,
    bool TakesAppointments);

public record StudioTrainerListResponse(IReadOnlyList<StudioTrainerDto> Trainers);

public record StudioErrorResponse(int StatusCode, string Reason);
