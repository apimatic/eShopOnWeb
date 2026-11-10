using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.PublicApi.Mindbody;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Details the studio asks of every new client, given with the first booking.</summary>
public class NewClientRequest
{
    public int? HomeLocationId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? MobileNumber { get; set; }
    public string? ReferralSource { get; set; }

    public NewClientDetails ToDetails() => new()
    {
        HomeLocationId = HomeLocationId,
        FirstName = FirstName,
        LastName = LastName,
        StreetAddress = StreetAddress,
        City = City,
        State = State,
        PostalCode = PostalCode,
        DateOfBirth = DateOfBirth,
        MobileNumber = MobileNumber,
        ReferralSource = ReferralSource
    };
}

public class ClassBookingRequest : NewClientRequest
{
    public int ClassId { get; set; }
}

public class AppointmentBookingRequest : NewClientRequest
{
    public int SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time with a UTC offset, as returned by the slots endpoint.</summary>
    public string? StartTime { get; set; }
    public int? LocationId { get; set; }
    public long? StaffId { get; set; }
}

public class AdminClassBookingRequest : NewClientRequest
{
    /// <summary>The shopper's eShop user name or e-mail address.</summary>
    public string? Shopper { get; set; }
    public int ClassId { get; set; }
}

public class ClassListResponse : BaseResponse
{
    public ClassListResponse(Guid correlationId) : base(correlationId) { }
    public List<StudioClass> Classes { get; set; } = new();
}

public class SlotListResponse : BaseResponse
{
    public SlotListResponse(Guid correlationId) : base(correlationId) { }
    public int SessionTypeId { get; set; }
    public List<StudioSlot> Slots { get; set; } = new();
}

public class TrainerListResponse : BaseResponse
{
    public TrainerListResponse(Guid correlationId) : base(correlationId) { }
    public List<StudioTrainer> Trainers { get; set; } = new();
}

public class BookingResponse : BaseResponse
{
    public BookingResponse(Guid correlationId, StudioBooking booking) : base(correlationId)
    {
        Booking = booking;
    }
    public StudioBooking Booking { get; set; }
}

internal static class StudioParsing
{
    private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DateTimeOffset ParseStartTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new StudioException(HttpStatusCode.BadRequest, "'startTime' is required.");
        value = value.Trim();
        if (!HasOffset.IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new StudioException(HttpStatusCode.BadRequest, "'startTime' must be an ISO-8601 date-time with a UTC offset, e.g. 2026-11-12T11:00:00-08:00.");
        return parsed;
    }
}
