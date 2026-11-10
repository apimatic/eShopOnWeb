using System;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

internal static class StudioAuth
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public static string Identity(HttpContext ctx) =>
        ctx.User.Identity?.Name ?? throw new StudioException(401, "Not signed in.");
}

/// <summary>Lists the classes shoppers can attend.</summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async
            (DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListClassesAsync(from, to, ct)))
            .Produces<ClassDto[]>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Lists the times a session of a type can start.</summary>
public class StudioSlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async
            (int sessionTypeId, DateOnly from, DateOnly to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListSlotsAsync(sessionTypeId, from, to, ct)))
            .Produces<SlotDto[]>()
            .WithTags("StudioEndpoints");
    }
}

public class StudioClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async
            (BookClassRequest request, HttpContext ctx, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.BookClassAsync(StudioAuth.Identity(ctx), request.ClassId, request, ct)))
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }
}

public class StudioAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async
            (BookAppointmentRequest request, HttpContext ctx, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.BookAppointmentAsync(StudioAuth.Identity(ctx), request.SessionTypeId,
                    request.StartTime ?? "", request.LocationId, request.StaffId, request, ct)))
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }
}

public class StudioClassBookingCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async
            (long bookingId, HttpContext ctx, StudioService studio, CancellationToken ct) =>
            {
                await studio.CancelClassBookingAsync(StudioAuth.Identity(ctx), bookingId, ct);
                return Results.NoContent();
            })
            .WithTags("StudioEndpoints");
    }
}

public class StudioAppointmentBookingCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async
            (long bookingId, HttpContext ctx, StudioService studio, CancellationToken ct) =>
            {
                await studio.CancelAppointmentBookingAsync(StudioAuth.Identity(ctx), bookingId, ct);
                return Results.NoContent();
            })
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Front desk: every trainer with all contact details.</summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioAuth.Scheme)] async
            (StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListTrainersAsync(ct)))
            .Produces<TrainerDto[]>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Front desk: place a shopper in any class in the timetable.</summary>
public class StudioAdminClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioAuth.Scheme)] async
            (AdminBookClassRequest request, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.AdminBookClassAsync(request.Shopper ?? "", request.ClassId, ct)))
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }
}
