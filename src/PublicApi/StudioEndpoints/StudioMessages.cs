using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>A studio request. Who the caller is comes from the JWT, never from the request body.</summary>
public class StudioRequest : BaseRequest
{
    public ClaimsPrincipal? Caller { get; init; }
    public CancellationToken Cancellation { get; init; }
}

public class ListStudioClassesRequest : StudioRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public class ListStudioSlotsRequest : StudioRequest
{
    public int? SessionTypeId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public class StudioClassBookingRequest : StudioRequest
{
    public ClassBookingBody? Body { get; init; }
}

public class StudioAppointmentBookingRequest : StudioRequest
{
    public AppointmentBookingBody? Body { get; init; }
}

public class StudioCancelBookingRequest : StudioRequest
{
    public long BookingId { get; init; }
}

public class StudioAdminClassBookingRequest : StudioRequest
{
    public AdminClassBookingBody? Body { get; init; }
}

public class ListStudioClassesResponse : BaseResponse
{
    public ListStudioClassesResponse(Guid correlationId) : base(correlationId) { }
    public ListStudioClassesResponse() { }
    public List<StudioClassDto> Classes { get; set; } = new();
}

public class ListStudioSlotsResponse : BaseResponse
{
    public ListStudioSlotsResponse(Guid correlationId) : base(correlationId) { }
    public ListStudioSlotsResponse() { }
    public List<StudioSlotDto> Slots { get; set; } = new();
}

public class StudioBookingResponse : BaseResponse
{
    public StudioBookingResponse(Guid correlationId) : base(correlationId) { }
    public StudioBookingResponse() { }
    public StudioBookingDto Booking { get; set; } = new();
}

public class StudioCancellationResponse : BaseResponse
{
    public StudioCancellationResponse(Guid correlationId) : base(correlationId) { }
    public StudioCancellationResponse() { }
    public StudioCancellationDto Cancellation { get; set; } = new();
}

public class ListStudioTrainersResponse : BaseResponse
{
    public ListStudioTrainersResponse(Guid correlationId) : base(correlationId) { }
    public ListStudioTrainersResponse() { }
    public List<StudioTrainerDto> Trainers { get; set; } = new();
}
