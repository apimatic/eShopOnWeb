using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The details the studio asks of every new client. Only needed with a shopper's first booking.
/// </summary>
public class ClientDetailsRequest
{
    /// <summary>The studio location the shopper chooses as their home location.</summary>
    public int? HomeLocationId { get; set; }

    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Date of birth, <c>yyyy-MM-dd</c>.</summary>
    public string? DateOfBirth { get; set; }

    public string? MobilePhone { get; set; }

    /// <summary>How the shopper heard of the studio (one of the studio's referral types).</summary>
    public string? ReferralSource { get; set; }

    /// <summary>Optional; derived from the shopper's eShop identity when omitted.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional; derived from the shopper's eShop identity when omitted.</summary>
    public string? LastName { get; set; }
}

public class ClassListingDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public long? TrainerId { get; set; }
    public string? TrainerName { get; set; }
    public int? Capacity { get; set; }
    public int? Booked { get; set; }
    public bool IsWaitlistAvailable { get; set; }
}

public class SlotDto
{
    public int SessionTypeId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class BookingDto
{
    /// <summary>Use with the DELETE endpoint to cancel this booking.</summary>
    public long BookingId { get; set; }

    /// <summary><c>class</c> or <c>appointment</c>.</summary>
    public string Kind { get; set; } = "";

    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public long? TrainerId { get; set; }
    public int? LocationId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }

    /// <summary>True when the shopper already held this booking (for example when the app repeats a request).</summary>
    public bool AlreadyBooked { get; set; }
}

public class CancellationDto
{
    public long BookingId { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "cancelled";
}

public class TrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public bool TeachesClasses { get; set; }
    public bool OffersAppointments { get; set; }
}
