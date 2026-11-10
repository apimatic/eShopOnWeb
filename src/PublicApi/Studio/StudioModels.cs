using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Studio;

public class StudioClassDto
{
    public long ClassId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? Trainer { get; set; }
}

public class StudioSlotDto
{
    public int SessionTypeId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int DurationMinutes { get; set; }
}

public class StudioBookingDto
{
    public long BookingId { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public long? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public long? StaffId { get; set; }

    /// <summary>True when the place had already been booked (for example by an earlier attempt of the same request).</summary>
    public bool AlreadyBooked { get; set; }
}

public class StudioCancellationDto
{
    public long BookingId { get; set; }
    public string Status { get; set; } = "cancelled";
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
/// The details the studio asks of every new client. They are only needed with a shopper's first booking.
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
    public string? ReferredBy { get; set; }
}

public class ClassBookingRequest : NewClientDetails
{
    public long ClassId { get; set; }

    // Alternative spellings accepted for the new-client details.
    [JsonPropertyName("locationId")] public int? LocationIdAlias { get => HomeLocationId; set => HomeLocationId ??= value; }
    [JsonPropertyName("address")] public string? AddressAlias { get => StreetAddress; set => StreetAddress ??= value; }
    [JsonPropertyName("addressLine1")] public string? AddressLine1Alias { get => StreetAddress; set => StreetAddress ??= value; }
    [JsonPropertyName("zip")] public string? ZipAlias { get => PostalCode; set => PostalCode ??= value; }
    [JsonPropertyName("zipCode")] public string? ZipCodeAlias { get => PostalCode; set => PostalCode ??= value; }
    [JsonPropertyName("birthDate")] public string? BirthDateAlias { get => DateOfBirth; set => DateOfBirth ??= value; }
    [JsonPropertyName("phone")] public string? PhoneAlias { get => MobilePhone; set => MobilePhone ??= value; }
    [JsonPropertyName("referral")] public string? ReferralAlias { get => ReferredBy; set => ReferredBy ??= value; }
    [JsonPropertyName("howHeard")] public string? HowHeardAlias { get => ReferredBy; set => ReferredBy ??= value; }
    [JsonPropertyName("howDidYouHear")] public string? HowDidYouHearAlias { get => ReferredBy; set => ReferredBy ??= value; }
}

public class AppointmentBookingRequest : NewClientDetails
{
    public int SessionTypeId { get; set; }
    public string? StartTime { get; set; }

    /// <summary>Optional; otherwise the shopper's home location is preferred, then any location with a free trainer.</summary>
    public int? LocationId { get; set; }

    /// <summary>Optional trainer.</summary>
    public long? StaffId { get; set; }

    [JsonPropertyName("homeLocation")] public int? HomeLocationAlias { get => HomeLocationId; set => HomeLocationId ??= value; }
    [JsonPropertyName("address")] public string? AddressAlias { get => StreetAddress; set => StreetAddress ??= value; }
    [JsonPropertyName("addressLine1")] public string? AddressLine1Alias { get => StreetAddress; set => StreetAddress ??= value; }
    [JsonPropertyName("zip")] public string? ZipAlias { get => PostalCode; set => PostalCode ??= value; }
    [JsonPropertyName("zipCode")] public string? ZipCodeAlias { get => PostalCode; set => PostalCode ??= value; }
    [JsonPropertyName("birthDate")] public string? BirthDateAlias { get => DateOfBirth; set => DateOfBirth ??= value; }
    [JsonPropertyName("phone")] public string? PhoneAlias { get => MobilePhone; set => MobilePhone ??= value; }
    [JsonPropertyName("referral")] public string? ReferralAlias { get => ReferredBy; set => ReferredBy ??= value; }
    [JsonPropertyName("howHeard")] public string? HowHeardAlias { get => ReferredBy; set => ReferredBy ??= value; }
    [JsonPropertyName("howDidYouHear")] public string? HowDidYouHearAlias { get => ReferredBy; set => ReferredBy ??= value; }
}

public class AdminClassBookingRequest
{
    /// <summary>The shopper's eShop username (their e-mail) or their studio client id.</summary>
    public string? Shopper { get; set; }
    public long ClassId { get; set; }
}
