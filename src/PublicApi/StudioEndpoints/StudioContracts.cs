using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public class ListStudioClassesResponse : BaseResponse
{
    public ListStudioClassesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<StudioClassDto> Classes { get; set; } = new();
}

public class ListStudioSlotsResponse : BaseResponse
{
    public ListStudioSlotsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<StudioSlotDto> Slots { get; set; } = new();
}

public class ListStudioTrainersResponse : BaseResponse
{
    public ListStudioTrainersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<StudioTrainerDto> Trainers { get; set; } = new();
}

public class StudioBookingResponse : BaseResponse
{
    public StudioBookingResponse(Guid correlationId, StudioBookingDto booking) : base(correlationId)
    {
        BookingId = booking.BookingId;
        Kind = booking.Kind;
        ClassId = booking.ClassId;
        SessionTypeId = booking.SessionTypeId;
        TrainerId = booking.TrainerId;
        LocationId = booking.LocationId;
        StartTime = booking.StartTime;
        EndTime = booking.EndTime;
        AlreadyBooked = booking.AlreadyBooked;
    }

    public long BookingId { get; }
    public string Kind { get; }
    public int? ClassId { get; }
    public int? SessionTypeId { get; }
    public long? TrainerId { get; }
    public int? LocationId { get; }
    public DateTimeOffset StartTime { get; }
    public DateTimeOffset EndTime { get; }
    public bool AlreadyBooked { get; }
}

public class CancelStudioBookingResponse : BaseResponse
{
    public CancelStudioBookingResponse(Guid correlationId, long bookingId) : base(correlationId)
    {
        BookingId = bookingId;
    }

    public long BookingId { get; }
    public string Status { get; } = "cancelled";
}

/// <summary>
/// The details the studio asks of every new client. They are needed with a shopper's first booking only.
/// </summary>
public class StudioClientDetailsRequest : BaseRequest
{
    /// <summary>Studio location chosen as home location.</summary>
    public int? HomeLocationId { get; set; }
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Date of birth, yyyy-MM-dd.</summary>
    public string? DateOfBirth { get; set; }
    public string? MobilePhone { get; set; }
    /// <summary>How the shopper heard of the studio (one of the studio's referral types).</summary>
    public string? ReferredBy { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // Accepted spellings of the same fields.
    public string? AddressLine1 { get; set; }
    public string? Address { get; set; }
    public string? BirthDate { get; set; }
    public string? MobileNumber { get; set; }
    public string? Phone { get; set; }
    public string? HowDidYouHear { get; set; }
    public string? ReferralSource { get; set; }

    public NewClientDetails ToDetails() => new()
    {
        HomeLocationId = HomeLocationId,
        StreetAddress = StreetAddress ?? AddressLine1 ?? Address,
        City = City,
        State = State,
        PostalCode = PostalCode,
        DateOfBirth = DateOfBirth ?? BirthDate,
        MobilePhone = MobilePhone ?? MobileNumber ?? Phone,
        ReferredBy = ReferredBy ?? HowDidYouHear ?? ReferralSource,
        FirstName = FirstName,
        LastName = LastName
    };
}

public class BookStudioClassRequest : StudioClientDetailsRequest
{
    public int? ClassId { get; set; }
}

public class BookStudioAppointmentRequest : StudioClientDetailsRequest
{
    public int? SessionTypeId { get; set; }
    /// <summary>Start of the session, ISO-8601 with UTC offset.</summary>
    public DateTimeOffset? StartTime { get; set; }
    /// <summary>Optional trainer; when absent any trainer free at that time is used.</summary>
    public long? TrainerId { get; set; }
    public long? StaffId { get; set; }
    /// <summary>Optional studio location of the session.</summary>
    public int? LocationId { get; set; }
}

public class AdminBookStudioClassRequest : BaseRequest
{
    /// <summary>The shopper's eShop identity (user name).</summary>
    public string? Shopper { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public int? ClassId { get; set; }
}
