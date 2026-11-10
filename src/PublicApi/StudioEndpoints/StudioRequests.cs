using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// The details the studio asks of every new client. They are only needed with a shopper's first booking.
/// A few alternative spellings of the field names are accepted.
/// </summary>
public abstract class StudioBookingRequest : BaseRequest
{
    public int? HomeLocationId { get; set; }
    public int? LocationId { get; set; }

    public string? StreetAddress { get; set; }
    public string? AddressLine1 { get; set; }
    public string? Address { get; set; }

    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    public string? DateOfBirth { get; set; }
    public string? BirthDate { get; set; }

    public string? MobilePhone { get; set; }
    public string? MobileNumber { get; set; }

    public string? ReferredBy { get; set; }
    public string? ReferralSource { get; set; }
    public string? HowHeardOfStudio { get; set; }

    public StudioClientProfile ToProfile() => new()
    {
        HomeLocationId = HomeLocationId ?? LocationId,
        AddressLine1 = First(StreetAddress, AddressLine1, Address),
        City = City,
        State = State,
        PostalCode = PostalCode,
        BirthDate = First(DateOfBirth, BirthDate),
        MobilePhone = First(MobilePhone, MobileNumber),
        ReferredBy = First(ReferredBy, ReferralSource, HowHeardOfStudio),
    };

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

public class BookClassRequest : StudioBookingRequest
{
    public int? ClassId { get; set; }
}

public class BookAppointmentRequest : StudioBookingRequest
{
    public int? SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time with a UTC offset, as returned by the session times.</summary>
    public string? StartTime { get; set; }

    /// <summary>Optional: a particular trainer. When omitted a trainer who is free then is chosen.</summary>
    public long? TrainerId { get; set; }
}

public class AdminBookClassRequest : BaseRequest
{
    /// <summary>The shopper: their e-mail address (their eShop user name).</summary>
    public string? Shopper { get; set; }
    public int? ClassId { get; set; }
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

public class StudioBookingResponse : BaseResponse
{
    public StudioBookingResponse(Guid correlationId) : base(correlationId)
    {
    }

    public StudioBookingResponse()
    {
    }

    public StudioBookingDto? Booking { get; set; }
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
