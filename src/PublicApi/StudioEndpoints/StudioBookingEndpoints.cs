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

internal static class StudioCaller
{
    /// <summary>The caller's eShop user name, taken from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name)
        ?? throw StudioException.BadRequest("The token does not identify a user.");

    public static IResult Booking((StudioBookingDto Booking, bool Created) result) =>
        result.Created ? Results.Created($"api/studio/{result.Booking.Kind}-bookings/{result.Booking.BookingId}", result.Booking)
                       : Results.Ok(result.Booking);
}

/// <summary>
/// Books the signed-in shopper into a class
/// </summary>
public class StudioClassBookingEndpoint : IEndpoint
{

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClassBookingRequest request, ClaimsPrincipal user, StudioService service, CancellationToken cancellationToken) =>
            {
                var result = await service.BookClassAsync(StudioCaller.UserName(user), request, cancellationToken);
                return StudioCaller.Booking(result);
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AppointmentBookingRequest request, ClaimsPrincipal user, StudioService service, CancellationToken cancellationToken) =>
            {
                var result = await service.BookAppointmentAsync(StudioCaller.UserName(user), request, cancellationToken);
                return StudioCaller.Booking(result);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }

}

public class StudioCancelRequest
{
    public long BookingId { get; set; }
}

/// <summary>
/// Cancels a class booking of the signed-in shopper
/// </summary>
public class StudioCancelClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService service, CancellationToken cancellationToken) =>
            {
                await service.CancelClassBookingAsync(StudioCaller.UserName(user), bookingId, cancellationToken);
                return Results.NoContent();
            })
            .WithTags("StudioEndpoints");
    }

}

/// <summary>
/// Cancels a one-to-one session booking of the signed-in shopper
/// </summary>
public class StudioCancelAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService service, CancellationToken cancellationToken) =>
            {
                await service.CancelAppointmentBookingAsync(StudioCaller.UserName(user), bookingId, cancellationToken);
                return Results.NoContent();
            })
            .WithTags("StudioEndpoints");
    }

}
