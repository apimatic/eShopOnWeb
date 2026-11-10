using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Lists the studio classes shoppers can attend, for a range of days (today and the next 7 days by default)
/// </summary>
public class StudioClassListEndpoint : IEndpoint<IResult, ListStudioClassesRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                return await HandleAsync(new ListStudioClassesRequest
                {
                    From = StudioDates.Parse(from, "from"),
                    To = StudioDates.Parse(to, "to"),
                    Cancellation = ct,
                }, studio);
            })
            .Produces<ListStudioClassesResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(ListStudioClassesRequest request, StudioService studio)
    {
        var response = new ListStudioClassesResponse(request.CorrelationId());
        response.Classes.AddRange(await studio.ListClassesAsync(request.From, request.To, request.Cancellation));
        return Results.Ok(response);
    }
}

/// <summary>
/// Lists the times a one-to-one session of a type can start on each day of a range, whether or not a trainer is free
/// </summary>
public class StudioSlotListEndpoint : IEndpoint<IResult, ListStudioSlotsRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? sessionTypeId, string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                return await HandleAsync(new ListStudioSlotsRequest
                {
                    SessionTypeId = sessionTypeId,
                    From = StudioDates.Parse(from, "from"),
                    To = StudioDates.Parse(to, "to"),
                    Cancellation = ct,
                }, studio);
            })
            .Produces<ListStudioSlotsResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(ListStudioSlotsRequest request, StudioService studio)
    {
        if (request.SessionTypeId is null || request.From is null || request.To is null)
        {
            throw StudioException.BadRequest("sessionTypeId, from and to are all required.");
        }

        var response = new ListStudioSlotsResponse(request.CorrelationId());
        response.Slots.AddRange(await studio.ListSlotsAsync(request.SessionTypeId.Value, request.From.Value, request.To.Value, request.Cancellation));
        return Results.Ok(response);
    }
}

/// <summary>
/// Books the signed-in shopper into a class
/// </summary>
public class StudioClassBookingEndpoint : IEndpoint<IResult, StudioClassBookingRequest, StudioApi>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClassBookingBody? body, ClaimsPrincipal user, StudioApi api) =>
            {
                return await HandleAsync(new StudioClassBookingRequest { Body = body, Caller = user }, api);
            })
            .Produces<StudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(StudioClassBookingRequest request, StudioApi api)
    {
        if (request.Body is null || request.Body.ClassId <= 0)
        {
            throw StudioException.BadRequest("classId is required.");
        }

        var shopper = await api.GetCallerAsync(request.Caller);
        // Once started, a booking runs to its end even if the app hangs up: its retry then finds the booking made.
        var booking = await api.Studio.BookClassAsync(shopper, request.Body.ClassId, request.Body, CancellationToken.None);
        return Results.Ok(new StudioBookingResponse(request.CorrelationId()) { Booking = booking });
    }
}

/// <summary>
/// Books the signed-in shopper into a one-to-one session
/// </summary>
public class StudioAppointmentBookingEndpoint : IEndpoint<IResult, StudioAppointmentBookingRequest, StudioApi>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AppointmentBookingBody? body, ClaimsPrincipal user, StudioApi api) =>
            {
                return await HandleAsync(new StudioAppointmentBookingRequest { Body = body, Caller = user }, api);
            })
            .Produces<StudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(StudioAppointmentBookingRequest request, StudioApi api)
    {
        if (request.Body is null || request.Body.SessionTypeId <= 0)
        {
            throw StudioException.BadRequest("sessionTypeId is required.");
        }

        var shopper = await api.GetCallerAsync(request.Caller);
        var booking = await api.Studio.BookAppointmentAsync(shopper, request.Body, CancellationToken.None);
        return Results.Ok(new StudioBookingResponse(request.CorrelationId()) { Booking = booking });
    }
}

/// <summary>
/// Cancels the signed-in shopper's own class booking
/// </summary>
public class StudioCancelClassBookingEndpoint : IEndpoint<IResult, StudioCancelBookingRequest, StudioApi>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioApi api) =>
            {
                return await HandleAsync(new StudioCancelBookingRequest { BookingId = bookingId, Caller = user }, api);
            })
            .Produces<StudioCancellationResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(StudioCancelBookingRequest request, StudioApi api)
    {
        var shopper = await api.GetCallerAsync(request.Caller);
        var result = await api.Studio.CancelClassBookingAsync(shopper, request.BookingId, CancellationToken.None);
        return Results.Ok(new StudioCancellationResponse(request.CorrelationId()) { Cancellation = result });
    }
}

/// <summary>
/// Cancels the signed-in shopper's own one-to-one session booking
/// </summary>
public class StudioCancelAppointmentBookingEndpoint : IEndpoint<IResult, StudioCancelBookingRequest, StudioApi>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioApi api) =>
            {
                return await HandleAsync(new StudioCancelBookingRequest { BookingId = bookingId, Caller = user }, api);
            })
            .Produces<StudioCancellationResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(StudioCancelBookingRequest request, StudioApi api)
    {
        var shopper = await api.GetCallerAsync(request.Caller);
        var result = await api.Studio.CancelAppointmentBookingAsync(shopper, request.BookingId, CancellationToken.None);
        return Results.Ok(new StudioCancellationResponse(request.CorrelationId()) { Cancellation = result });
    }
}
