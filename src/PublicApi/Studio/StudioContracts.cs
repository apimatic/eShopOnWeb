using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Studio;

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
}

public class StudioSlotDto
{
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class StudioTrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? HomePhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
    public bool Active { get; set; }
    public bool ClassTeacher { get; set; }
    public bool AppointmentInstructor { get; set; }
}

public class StudioBookingDto
{
    public long BookingId { get; set; }
    public string Kind { get; set; } = "";
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public long? StaffId { get; set; }
    public string ClientId { get; set; } = "";
}

/// <summary>
/// What the studio asks of every new client. Only needed with a shopper's first booking.
/// The alternative property names are accepted for convenience and feed the same values.
/// </summary>
public class NewClientDetails
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
    /// <summary>How the shopper heard of the studio.</summary>
    public string? ReferralSource { get; set; }

    public string? AddressLine1 { get => StreetAddress; set => StreetAddress ??= value; }
    public string? Address { get => StreetAddress; set => StreetAddress ??= value; }
    public string? BirthDate { get => DateOfBirth; set => DateOfBirth ??= value; }
    public string? Phone { get => MobilePhone; set => MobilePhone ??= value; }
    public string? ReferredBy { get => ReferralSource; set => ReferralSource ??= value; }
    public string? HowHeard { get => ReferralSource; set => ReferralSource ??= value; }
    public string? Zip { get => PostalCode; set => PostalCode ??= value; }
    public int? LocationId { get => HomeLocationId; set => HomeLocationId ??= value; }
}

public class BookClassRequest : NewClientDetails
{
    public int ClassId { get; set; }
}

public class BookAppointmentRequest : NewClientDetails
{
    public int SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time with its UTC offset, e.g. 2026-11-12T07:00:00-08:00.</summary>
    public string? StartTime { get; set; }

    /// <summary>Optionally pick a trainer.</summary>
    public long? StaffId { get; set; }

    /// <summary>Optionally pick the location of the session (otherwise any where a trainer is free).</summary>
    public int? SessionLocationId { get; set; }
}

public class AdminBookClassRequest : NewClientDetails
{
    /// <summary>The shopper's eShop user name (their e-mail address).</summary>
    public string? Shopper { get; set; }
    public int ClassId { get; set; }
}

public class CancelResult
{
    public long BookingId { get; set; }
    public string Status { get; set; } = "Cancelled";
}
