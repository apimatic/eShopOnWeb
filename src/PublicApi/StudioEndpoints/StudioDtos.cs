using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

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
    public int? Capacity { get; set; }
    public int? SpotsLeft { get; set; }
}

public class StudioSlotDto
{
    public int SessionTypeId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class StudioBookingDto
{
    /// <summary>Id to use for cancelling: the visit id of a class booking, the appointment id of a session.</summary>
    public long BookingId { get; set; }
    public string Kind { get; set; } = "";
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public long? TrainerId { get; set; }
    public string ClientId { get; set; } = "";
}

public class StudioTrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool Active { get; set; }
    public bool ClassTeacher { get; set; }
    public bool AppointmentInstructor { get; set; }
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

/// <summary>
/// What the studio asks of every new client. Only needed with a shopper's first booking.
/// </summary>
public class StudioClientProfile
{
    public int? HomeLocationId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? DateOfBirth { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }

    // Accepted under their Mindbody names as well.
    public string? AddressLine1 { set => StreetAddress ??= value; }
    public string? BirthDate { set => DateOfBirth ??= value; }
    public string? ReferralSource { set => ReferredBy ??= value; }
    public string? HowHeardAboutStudio { set => ReferredBy ??= value; }
    public int? LocationId { set => HomeLocationId ??= value; }
}

public class ClassBookingRequest : StudioClientProfile
{
    public int ClassId { get; set; }
}

public class AppointmentBookingRequest : StudioClientProfile
{
    public int SessionTypeId { get; set; }

    /// <summary>When the session starts. Must be one of the times returned by the slots endpoint.</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>Optional: a particular trainer.</summary>
    public long? TrainerId { get; set; }

    /// <summary>Optional: where the session takes place; defaults to the shopper's home location.</summary>
    public int? SessionLocationId { get; set; }
}

public class AdminClassBookingRequest
{
    /// <summary>The shopper's eShop user name.</summary>
    public string? Shopper { get; set; }
    public int ClassId { get; set; }

    public string? Email { set => Shopper ??= value; }
    public string? UserName { set => Shopper ??= value; }
}
