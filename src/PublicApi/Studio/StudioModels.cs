using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The details the studio asks of every new client. Only needed with a shopper's first booking.
/// A few alternative property names are accepted so callers can use the studio's own wording.
/// </summary>
public class NewClientDetails
{
    public int? HomeLocationId { get; set; }

    [JsonPropertyName("locationId")]
    public int? LocationIdAlias { set => HomeLocationId ??= value; }

    public string? StreetAddress { get; set; }

    [JsonPropertyName("addressLine1")]
    public string? AddressLine1Alias { set => StreetAddress ??= value; }

    [JsonPropertyName("address")]
    public string? AddressAlias { set => StreetAddress ??= value; }

    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Date of birth, <c>yyyy-MM-dd</c>.</summary>
    public DateOnly? DateOfBirth { get; set; }

    [JsonPropertyName("birthDate")]
    public DateOnly? BirthDateAlias { set => DateOfBirth ??= value; }

    public string? MobilePhone { get; set; }

    [JsonPropertyName("mobileNumber")]
    public string? MobileNumberAlias { set => MobilePhone ??= value; }

    [JsonPropertyName("phone")]
    public string? PhoneAlias { set => MobilePhone ??= value; }

    /// <summary>How the client heard of the studio (one of the studio's referral types).</summary>
    public string? ReferredBy { get; set; }

    [JsonPropertyName("howHeard")]
    public string? HowHeardAlias { set => ReferredBy ??= value; }

    [JsonPropertyName("referralSource")]
    public string? ReferralSourceAlias { set => ReferredBy ??= value; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class ClassBookingRequest : NewClientDetails
{
    public int ClassId { get; set; }
}

public class AppointmentBookingRequest : NewClientDetails
{
    public int SessionTypeId { get; set; }

    /// <summary>Start of the session, ISO-8601 with UTC offset (one of the times listed by the slots endpoint).</summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>Optional: a specific trainer.</summary>
    public long? TrainerId { get; set; }

    [JsonPropertyName("staffId")]
    public long? StaffIdAlias { set => TrainerId ??= value; }
}

public class AdminClassBookingRequest : NewClientDetails
{
    /// <summary>The shopper's eShop identity (user name / e-mail).</summary>
    public string? Shopper { get; set; }

    [JsonPropertyName("userName")]
    public string? UserNameAlias { set => Shopper ??= value; }

    [JsonPropertyName("email")]
    public string? EmailAlias { set => Shopper ??= value; }

    [JsonPropertyName("shopperEmail")]
    public string? ShopperEmailAlias { set => Shopper ??= value; }

    public int ClassId { get; set; }
}

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
    public int? MaxCapacity { get; set; }
    public int? SpotsAvailable { get; set; }
}

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
    public bool Active { get; set; }
    public bool TeachesClasses { get; set; }
    public bool TakesAppointments { get; set; }
}

public class ClassBookingDto
{
    public long BookingId { get; set; }
    public int ClassId { get; set; }
    public string? ClassName { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public int? LocationId { get; set; }
    public string ClientId { get; set; } = "";

    /// <summary>True when the shopper already held this booking (a repeated request) and nothing new was created.</summary>
    public bool AlreadyBooked { get; set; }
}

public class AppointmentBookingDto
{
    public long BookingId { get; set; }
    public int? SessionTypeId { get; set; }
    public string? SessionTypeName { get; set; }
    public long? TrainerId { get; set; }
    public int? LocationId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public string ClientId { get; set; } = "";
    public bool AlreadyBooked { get; set; }
}

public class CancelBookingDto
{
    public long BookingId { get; set; }

    /// <summary>Always <c>Cancelled</c>: cancelling a booking that is already cancelled succeeds too.</summary>
    public string Status { get; set; } = "Cancelled";
}

public class ListStudioClassesResponse : BaseResponse
{
    public ListStudioClassesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListStudioClassesResponse()
    {
    }

    public List<StudioClassDto> Classes { get; set; } = new();
}

public class ListStudioSlotsResponse : BaseResponse
{
    public ListStudioSlotsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListStudioSlotsResponse()
    {
    }

    public List<StudioSlotDto> Slots { get; set; } = new();
}

public class ListStudioTrainersResponse : BaseResponse
{
    public ListStudioTrainersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListStudioTrainersResponse()
    {
    }

    public List<StudioTrainerDto> Trainers { get; set; } = new();
}
