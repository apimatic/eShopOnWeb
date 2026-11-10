using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Books the signed-in shopper into a class. A repeated request for the same class returns the existing booking.
/// </summary>
public class StudioBookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookClassRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                if (request.ClassId is null)
                    throw new StudioException(400, "'classId' is required.");
                var shopper = await StudioShopperResolver.FromPrincipalAsync(userManager, user);
                return Results.Ok(await studio.BookClassAsync(shopper, request.ClassId.Value, request, ct));
            })
            .Produces<ClassBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books the signed-in shopper into a one-to-one session. A repeated request returns the existing booking.
/// </summary>
public class StudioBookAppointmentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookAppointmentRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                if (request.SessionTypeId is null)
                    throw new StudioException(400, "'sessionTypeId' is required.");
                if (request.StartTime is null)
                    throw new StudioException(400, "'startTime' is required (ISO-8601 date-time with UTC offset).");
                var shopper = await StudioShopperResolver.FromPrincipalAsync(userManager, user);
                return Results.Ok(await studio.BookAppointmentAsync(shopper, request.SessionTypeId.Value, request.StartTime.Value,
                    request.LocationId, request.StaffId, request, ct));
            })
            .Produces<AppointmentBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's own class booking. Cancelling again is also a success.
/// </summary>
public class StudioCancelClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                var shopper = await StudioShopperResolver.FromPrincipalAsync(userManager, user);
                return Results.Ok(await studio.CancelClassBookingAsync(shopper, bookingId, ct));
            })
            .Produces<CancelledBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's own one-to-one session booking. Cancelling again is also a success.
/// </summary>
public class StudioCancelAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                var shopper = await StudioShopperResolver.FromPrincipalAsync(userManager, user);
                return Results.Ok(await studio.CancelAppointmentAsync(shopper, bookingId, ct));
            })
            .Produces<CancelledBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
