using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Studio bookings (classes and one-to-one sessions), backed by Mindbody.
/// The caller's identity and role come from the JWT.
/// </summary>
public class StudioEndpoints : IEndpoint
{
    private const string TAG = "StudioEndpoints";
    private const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private const string JWT = JwtBearerDefaults.AuthenticationScheme;

    private static string Identity(ClaimsPrincipal user)
        => user.Identity?.Name ?? throw new StudioException(401, "The caller's identity is missing from the token.");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JWT)] async (DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.ListClassesAsync(from, to, ct)))
            .Produces<ClassDto[]>().WithTags(TAG);

        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JWT)] async (int? sessionTypeId, DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.ListSlotsAsync(sessionTypeId, from, to, ct)))
            .Produces<SlotDto[]>().WithTags(TAG);

        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JWT)] async (ClassBookingRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.BookClassAsync(Identity(user), request, ct)))
            .Produces<ClassBookingDto>().WithTags(TAG);

        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JWT)] async (AppointmentBookingRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.BookAppointmentAsync(Identity(user), request, ct)))
            .Produces<AppointmentBookingDto>().WithTags(TAG);

        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JWT)] async (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.CancelClassBookingAsync(Identity(user), bookingId, ct)))
            .Produces<CancelledDto>().WithTags(TAG);

        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JWT)] async (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.CancelAppointmentBookingAsync(Identity(user), bookingId, ct)))
            .Produces<CancelledDto>().WithTags(TAG);

        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = JWT)] async (StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.ListTrainersAsync(ct)))
            .Produces<TrainerDto[]>().WithTags(TAG);

        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = JWT)] async (AdminClassBookingRequest request, StudioService studio, CancellationToken ct)
                => Results.Ok(await studio.AdminBookClassAsync(request, ct)))
            .Produces<ClassBookingDto>().WithTags(TAG);
    }
}
