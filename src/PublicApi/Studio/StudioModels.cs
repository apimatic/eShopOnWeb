using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>A class shoppers can attend. Times carry the studio's UTC offset.</summary>
public class StudioClassDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? TrainerName { get; set; }
    public int? SpotsLeft { get; set; }
}

/// <summary>A time a session can start, with its end.</summary>
public class StudioSlotDto
{
    public int SessionTypeId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class StudioTrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool Active { get; set; }
    public string? Email { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class StudioBookingDto
{
    /// <summary>Id to use when cancelling the booking.</summary>
    public long BookingId { get; set; }
    public string Kind { get; set; } = "";
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public long? TrainerId { get; set; }
    public int? LocationId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    /// <summary>True when this request found the booking already in place (for example, a repeated request).</summary>
    public bool AlreadyBooked { get; set; }
}

/// <summary>Details the studio asks of every new client; needed with a shopper's first booking only.</summary>
public class NewClientDetails
{
    public int? HomeLocationId { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? DateOfBirth { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
