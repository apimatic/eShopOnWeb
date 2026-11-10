using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

internal static class StudioAuth
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    /// <summary>The caller's identity comes from the JWT; the e-mail address is the one of their eShop account.</summary>
    public static async Task<StudioCaller> ResolveCallerAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new StudioException(401, "The token does not identify a user.");
        }

        var user = await users.FindByNameAsync(name);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = name;
        }

        if (!email.Contains('@'))
        {
            throw StudioException.BadRequest("Your eShop account has no e-mail address, which the studio needs to register you.");
        }

        return new StudioCaller(name, email.Trim());
    }
}

/// <summary>Lists the studio's classes shoppers can attend</summary>
public class ListStudioClassesEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async (string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioClassesResponse();
                response.Classes.AddRange(await studio.ListClassesAsync(from, to, ct));
                return Results.Ok(response);
            })
            .Produces<ListStudioClassesResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Lists the times a one-to-one session can start, day by day</summary>
public class ListStudioSlotsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async (int? sessionTypeId, string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioSlotsResponse();
                response.Slots.AddRange(await studio.ListSlotsAsync(sessionTypeId, from, to, ct));
                return Results.Ok(response);
            })
            .Produces<ListStudioSlotsResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Books the caller into a class</summary>
public class BookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async (BookClassRequest request, ClaimsPrincipal user,
                UserManager<ApplicationUser> users, StudioService studio, CancellationToken ct) =>
            {
                var caller = await StudioAuth.ResolveCallerAsync(user, users);
                var booking = await studio.BookClassAsync(caller, request.ClassId, request.ToProfile(), ct);
                return Results.Created($"api/studio/class-bookings/{booking.BookingId}",
                    new StudioBookingResponse(request.CorrelationId()) { Booking = booking });
            })
            .Produces<StudioBookingResponse>(StatusCodes.Status201Created)
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Books the caller into a one-to-one session</summary>
public class BookAppointmentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async (BookAppointmentRequest request, ClaimsPrincipal user,
                UserManager<ApplicationUser> users, StudioService studio, CancellationToken ct) =>
            {
                var caller = await StudioAuth.ResolveCallerAsync(user, users);
                var booking = await studio.BookAppointmentAsync(caller, request.SessionTypeId, request.StartTime, request.TrainerId, request.ToProfile(), ct);
                return Results.Created($"api/studio/appointment-bookings/{booking.BookingId}",
                    new StudioBookingResponse(request.CorrelationId()) { Booking = booking });
            })
            .Produces<StudioBookingResponse>(StatusCodes.Status201Created)
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Cancels one of the caller's class bookings</summary>
public class CancelClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async (long bookingId, ClaimsPrincipal user,
                UserManager<ApplicationUser> users, StudioService studio, CancellationToken ct) =>
            {
                var caller = await StudioAuth.ResolveCallerAsync(user, users);
                await studio.CancelClassBookingAsync(caller, bookingId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Cancels one of the caller's one-to-one session bookings</summary>
public class CancelAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioAuth.Scheme)] async (long bookingId, ClaimsPrincipal user,
                UserManager<ApplicationUser> users, StudioService studio, CancellationToken ct) =>
            {
                var caller = await StudioAuth.ResolveCallerAsync(user, users);
                await studio.CancelAppointmentBookingAsync(caller, bookingId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Lists every trainer with all their contact details (front-desk operators)</summary>
public class ListStudioTrainersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = StudioAuth.Admins, AuthenticationSchemes = StudioAuth.Scheme)] async (StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioTrainersResponse();
                response.Trainers.AddRange(await studio.ListTrainersAsync(ct));
                return Results.Ok(response);
            })
            .Produces<ListStudioTrainersResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Places a shopper in a class (front-desk operators)</summary>
public class AdminBookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = StudioAuth.Admins, AuthenticationSchemes = StudioAuth.Scheme)] async (AdminBookClassRequest request,
                StudioService studio, CancellationToken ct) =>
            {
                var booking = await studio.BookClassForShopperAsync(request.Shopper, request.ClassId, ct);
                return Results.Created($"api/studio/class-bookings/{booking.BookingId}",
                    new StudioBookingResponse(request.CorrelationId()) { Booking = booking });
            })
            .Produces<StudioBookingResponse>(StatusCodes.Status201Created)
            .WithTags("StudioEndpoints");
    }
}
