using System;
using System.Net;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Books and cancels studio bookings; front-desk operators can also book a shopper into a class
/// </summary>
public class StudioBookingEndpoints : IEndpoint
{
    private const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClassBookingRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                var email = await StudioIdentity.GetShopperEmailAsync(user, userManager);
                var result = await studio.BookClassAsync(email, request.ClassId, request.ToDetails(), staffOverride: false, ct);
                return ToResult(result);
            })
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .Produces<BookingResponse>()
            .WithTags("StudioEndpoints");

        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AppointmentBookingRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                var startTime = StudioParsing.ParseStartTime(request.StartTime);
                var email = await StudioIdentity.GetShopperEmailAsync(user, userManager);
                var result = await studio.BookAppointmentAsync(email, request.SessionTypeId, startTime,
                    request.LocationId, request.StaffId, request.ToDetails(), ct);
                return ToResult(result);
            })
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .Produces<BookingResponse>()
            .WithTags("StudioEndpoints");

        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                var email = await StudioIdentity.GetShopperEmailAsync(user, userManager);
                await studio.CancelClassBookingAsync(email, bookingId, ct);
                return Results.NoContent();
            })
            .WithTags("StudioEndpoints");

        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                var email = await StudioIdentity.GetShopperEmailAsync(user, userManager);
                await studio.CancelAppointmentBookingAsync(email, bookingId, ct);
                return Results.NoContent();
            })
            .WithTags("StudioEndpoints");

        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminClassBookingRequest request, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Shopper))
                    throw new StudioException(HttpStatusCode.BadRequest, "'shopper' is required.");
                var shopper = request.Shopper.Trim();
                var user = await userManager.FindByNameAsync(shopper) ?? await userManager.FindByEmailAsync(shopper)
                           ?? throw new StudioException(HttpStatusCode.NotFound, $"Shopper '{shopper}' was not found.");
                var result = await studio.BookClassAsync(StudioIdentity.EmailOf(user), request.ClassId, request.ToDetails(), staffOverride: true, ct);
                return ToResult(result);
            })
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .Produces<BookingResponse>()
            .WithTags("StudioEndpoints");
    }

    // A retried booking that already succeeded answers 200 with the existing booking, so the app sees success.
    private static IResult ToResult(BookingResult result)
    {
        var body = new BookingResponse(Guid.NewGuid(), result.Booking);
        if (!result.Created) return Results.Ok(body);
        var route = result.Booking.Kind == "class" ? "class-bookings" : "appointment-bookings";
        return Results.Created($"api/studio/{route}/{result.Booking.BookingId}", body);
    }
}
