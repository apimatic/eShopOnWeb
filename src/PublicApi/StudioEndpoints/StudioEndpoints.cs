using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

internal static class StudioAuth
{
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's identity is the user name in the JWT.</summary>
    public static string? UserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
}

/// <summary>
/// Lists the classes shoppers can attend (default: today and the next 7 days).
/// </summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = StudioAuth.SCHEME)] async (string? from, string? to, StudioService studio) =>
            {
                DateOnly? f = string.IsNullOrWhiteSpace(from) ? null : StudioService.ParseDate(from, "from");
                DateOnly? t = string.IsNullOrWhiteSpace(to) ? null : StudioService.ParseDate(to, "to");
                var classes = await studio.ListClassesAsync(f, t);
                return Results.Ok(new ListStudioClassesResponse { Classes = classes.ToList() });
            })
            .Produces<ListStudioClassesResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Lists the times a session of a type can start on each day between two dates.
/// </summary>
public class StudioSlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = StudioAuth.SCHEME)] async (string? sessionTypeId, string? from, string? to, StudioService studio) =>
            {
                if (!int.TryParse(sessionTypeId, out var typeId) || typeId <= 0)
                {
                    throw StudioException.BadRequest("'sessionTypeId' is required and must be a session type number.");
                }
                var f = StudioService.ParseDate(from, "from");
                var t = StudioService.ParseDate(to, "to");
                var slots = await studio.ListSlotsAsync(typeId, f, t);
                return Results.Ok(new ListStudioSlotsResponse { SessionTypeId = typeId, Slots = slots.ToList() });
            })
            .Produces<ListStudioSlotsResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books the signed-in shopper into a class.
/// </summary>
public class StudioClassBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.SCHEME)] async (BookClassRequest? request, ClaimsPrincipal user, StudioService studio) =>
            {
                if (request == null) throw StudioException.BadRequest("A request body is required.");
                return Results.Ok(await studio.BookClassAsync(StudioAuth.UserName(user), request));
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books the signed-in shopper into a one-to-one session.
/// </summary>
public class StudioAppointmentBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.SCHEME)] async (BookAppointmentRequest? request, ClaimsPrincipal user, StudioService studio) =>
            {
                if (request == null) throw StudioException.BadRequest("A request body is required.");
                return Results.Ok(await studio.BookAppointmentAsync(StudioAuth.UserName(user), request));
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's own class booking.
/// </summary>
public class StudioClassBookingCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.SCHEME)] async (string bookingId, ClaimsPrincipal user, StudioService studio) =>
            {
                if (!long.TryParse(bookingId, out var id) || id <= 0) throw StudioException.BadRequest("'bookingId' must be a booking number.");
                return Results.Ok(await studio.CancelClassBookingAsync(StudioAuth.UserName(user), id));
            })
            .Produces<CancelResult>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's own one-to-one session booking.
/// </summary>
public class StudioAppointmentBookingCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.SCHEME)] async (string bookingId, ClaimsPrincipal user, StudioService studio) =>
            {
                if (!long.TryParse(bookingId, out var id) || id <= 0) throw StudioException.BadRequest("'bookingId' must be a booking number.");
                return Results.Ok(await studio.CancelAppointmentBookingAsync(StudioAuth.UserName(user), id));
            })
            .Produces<CancelResult>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Front desk: every trainer with all their contact details.
/// </summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioAuth.SCHEME)] async (StudioService studio) =>
            {
                var trainers = await studio.ListTrainersAsync();
                return Results.Ok(new ListStudioTrainersResponse { Trainers = trainers.ToList() });
            })
            .Produces<ListStudioTrainersResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Front desk: places a shopper in any class in the timetable.
/// </summary>
public class StudioAdminClassBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioAuth.SCHEME)] async (AdminBookClassRequest? request, StudioService studio) =>
            {
                if (request == null) throw StudioException.BadRequest("A request body is required.");
                return Results.Ok(await studio.BookClassForShopperAsync(request));
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
