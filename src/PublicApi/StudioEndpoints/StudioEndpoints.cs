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

internal static class StudioAuth
{
    public const string SHOPPER_SCHEME = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's eShop identity, taken from the JWT.</summary>
    public static string Identity(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? throw new StudioException(401, "The token does not identify a user.");
}

/// <summary>
/// Lists the classes shoppers can attend
/// </summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (string? from, string? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListClassesAsync(from, to, ct)))
            .Produces<System.Collections.Generic.List<StudioClassDto>>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Lists the times a one-to-one session can start
/// </summary>
public class StudioSlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (int sessionTypeId, string? from, string? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListSlotsAsync(sessionTypeId, from, to, ct)))
            .Produces<System.Collections.Generic.List<StudioSlotDto>>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books the signed-in shopper into a class
/// </summary>
public class StudioClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (ClassBookingRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.BookClassAsync(StudioAuth.Identity(user), request, ct);
                return created
                    ? Results.Created($"api/studio/class-bookings/{booking.BookingId}", booking)
                    : Results.Ok(booking);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books the signed-in shopper into a one-to-one session
/// </summary>
public class StudioAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (AppointmentBookingRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.BookAppointmentAsync(StudioAuth.Identity(user), request, ct);
                return created
                    ? Results.Created($"api/studio/appointment-bookings/{booking.BookingId}", booking)
                    : Results.Ok(booking);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's class booking
/// </summary>
public class StudioClassBookingCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.CancelClassBookingAsync(StudioAuth.Identity(user), bookingId, ct)))
            .Produces<StudioCancellationDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's one-to-one session booking
/// </summary>
public class StudioAppointmentBookingCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.CancelAppointmentBookingAsync(StudioAuth.Identity(user), bookingId, ct)))
            .Produces<StudioCancellationDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Lists every trainer with all their contact details (administrators only)
/// </summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListTrainersAsync(ct)))
            .Produces<System.Collections.Generic.List<StudioTrainerDto>>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Places a shopper in any class of the timetable (administrators only)
/// </summary>
public class StudioAdminClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioAuth.SHOPPER_SCHEME)] async
            (AdminClassBookingRequest request, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.BookClassForShopperAsync(request, ct);
                return created
                    ? Results.Created($"api/studio/class-bookings/{booking.BookingId}", booking)
                    : Results.Ok(booking);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
