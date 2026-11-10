using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>A signed-in eShop user, as the studio feature needs to know them.</summary>
public sealed record Shopper(string UserId, string UserName, string Email);

/// <summary>A class shoppers can attend. Times carry the studio's UTC offset.</summary>
public sealed class StudioClassDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? Trainer { get; set; }
    public int? SpotsLeft { get; set; }
}

/// <summary>A time a one-to-one session can start.</summary>
public sealed class StudioSlotDto
{
    public int SessionTypeId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public sealed class StudioBookingDto
{
    /// <summary>"class" or "appointment".</summary>
    public string Kind { get; set; } = "";
    public long BookingId { get; set; }
    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? Trainer { get; set; }

    /// <summary>False when the shopper already held this booking and nothing new was made.</summary>
    public bool Created { get; set; } = true;
}

public sealed class StudioCancellationDto
{
    public string Kind { get; set; } = "";
    public long BookingId { get; set; }
    public string Status { get; set; } = "cancelled";
}

/// <summary>A trainer with every contact detail the studio holds.</summary>
public sealed class StudioTrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool Active { get; set; }
    public bool TeachesClasses { get; set; }
    public bool TeachesAppointments { get; set; }
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
public class NewClientDetails
{
    /// <summary>The studio location that becomes the shopper's home location.</summary>
    public int? HomeLocationId { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? MobilePhone { get; set; }
    /// <summary>How the shopper heard of the studio.</summary>
    public string? ReferralSource { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // Alternative names the same details may arrive under.
    [JsonPropertyName("locationId")] public int? LocationIdAlias { set => HomeLocationId ??= value; }
    [JsonPropertyName("address")] public string? AddressAlias { set => StreetAddress ??= value; }
    [JsonPropertyName("addressLine1")] public string? AddressLine1Alias { set => StreetAddress ??= value; }
    [JsonPropertyName("street")] public string? StreetAlias { set => StreetAddress ??= value; }
    [JsonPropertyName("zip")] public string? ZipAlias { set => PostalCode ??= value; }
    [JsonPropertyName("zipCode")] public string? ZipCodeAlias { set => PostalCode ??= value; }
    [JsonPropertyName("birthDate")] public DateOnly? BirthDateAlias { set => DateOfBirth ??= value; }
    [JsonPropertyName("phone")] public string? PhoneAlias { set => MobilePhone ??= value; }
    [JsonPropertyName("mobile")] public string? MobileAlias { set => MobilePhone ??= value; }
    [JsonPropertyName("mobileNumber")] public string? MobileNumberAlias { set => MobilePhone ??= value; }
    [JsonPropertyName("referredBy")] public string? ReferredByAlias { set => ReferralSource ??= value; }
    [JsonPropertyName("howHeard")] public string? HowHeardAlias { set => ReferralSource ??= value; }
    [JsonPropertyName("referral")] public string? ReferralAlias { set => ReferralSource ??= value; }
}

public sealed class ClassBookingBody : NewClientDetails
{
    public int ClassId { get; set; }
}

public sealed class AppointmentBookingBody : NewClientDetails
{
    public int SessionTypeId { get; set; }
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>Optionally the trainer asked for. Otherwise any trainer who is free is used.</summary>
    public long? StaffId { get; set; }

    /// <summary>Optionally the location of the session. Otherwise the trainer's location is used.</summary>
    public int? SessionLocationId { get; set; }
}

public sealed class AdminClassBookingBody
{
    /// <summary>The shopper's eShop user name or email.</summary>
    public string? Shopper { get; set; }
    public int ClassId { get; set; }

    [JsonPropertyName("shopperEmail")] public string? ShopperEmailAlias { set => Shopper ??= value; }
    [JsonPropertyName("email")] public string? EmailAlias { set => Shopper ??= value; }
    [JsonPropertyName("userName")] public string? UserNameAlias { set => Shopper ??= value; }
}
