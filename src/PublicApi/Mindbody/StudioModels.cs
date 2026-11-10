using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Details the studio asks of every new client. Only needed with a shopper's first booking.
/// </summary>
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

public class ClassBookingRequest : NewClientDetails
{
    public long ClassId { get; set; }
}

public class AppointmentBookingRequest : NewClientDetails
{
    public int SessionTypeId { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public long? StaffId { get; set; }
}

public class AdminClassBookingRequest
{
    /// <summary>The shopper's eShop user name or email address.</summary>
    public string? Shopper { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public long ClassId { get; set; }
}

public class StudioClassDto
{
    public long ClassId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? TrainerName { get; set; }
    public int SpotsAvailable { get; set; }
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
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
}

public class StudioBookingDto
{
    public long BookingId { get; set; }
    public string Type { get; set; } = "";
    public long? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public long? StaffId { get; set; }
    public int? LocationId { get; set; }
    /// <summary>True when the shopper already held this booking (a repeated request).</summary>
    public bool AlreadyBooked { get; set; }
}

public class StudioCancelDto
{
    public long BookingId { get; set; }
    public string Status { get; set; } = "Cancelled";
}

public class ListStudioClassesResponse : BaseResponse
{
    public ListStudioClassesResponse(Guid correlationId, List<StudioClassDto> classes) : base(correlationId)
    {
        Classes = classes;
    }

    public List<StudioClassDto> Classes { get; set; }
}

public class ListStudioSlotsResponse : BaseResponse
{
    public ListStudioSlotsResponse(Guid correlationId, List<StudioSlotDto> slots) : base(correlationId)
    {
        Slots = slots;
    }

    public List<StudioSlotDto> Slots { get; set; }
}

public class ListStudioTrainersResponse : BaseResponse
{
    public ListStudioTrainersResponse(Guid correlationId, List<StudioTrainerDto> trainers) : base(correlationId)
    {
        Trainers = trainers;
    }

    public List<StudioTrainerDto> Trainers { get; set; }
}
