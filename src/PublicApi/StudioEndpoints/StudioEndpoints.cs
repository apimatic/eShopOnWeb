using System;
using System.Globalization;
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

internal static class StudioEndpointHelpers
{
    public const string SHOPPER_SCHEME = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's identity, from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value
        ?? throw new StudioException(System.Net.HttpStatusCode.Unauthorized, "The token carries no identity.");

    public static DateOnly? ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = value.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }
        // A full ISO-8601 date-time is accepted too; its calendar date is used.
        if (text.Length > 10 && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant))
        {
            return DateOnly.FromDateTime(instant.DateTime);
        }
        throw StudioException.BadRequest($"'{name}' must be a date such as 2026-05-31.");
    }

    public static IResult Booking<T>(T booking, bool alreadyBooked, string location) =>
        alreadyBooked ? Results.Ok(booking) : Results.Created(location, booking);
}

/// <summary>
/// Lists the studio's classes a shopper can attend
/// </summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioClassesResponse(Guid.NewGuid())
                {
                    Classes = await studio.ListClassesAsync(
                        StudioEndpointHelpers.ParseDate(from, "from"), StudioEndpointHelpers.ParseDate(to, "to"), ct),
                };
                return Results.Ok(response);
            })
            .Produces<ListStudioClassesResponse>()
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
            [Authorize(AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (int? sessionTypeId, string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                if (sessionTypeId is null or <= 0)
                {
                    throw StudioException.BadRequest("sessionTypeId is required.");
                }
                var start = StudioEndpointHelpers.ParseDate(from, "from") ?? throw StudioException.BadRequest("'from' is required.");
                var end = StudioEndpointHelpers.ParseDate(to, "to") ?? throw StudioException.BadRequest("'to' is required.");
                var response = new ListStudioSlotsResponse(Guid.NewGuid())
                {
                    Slots = await studio.ListSlotsAsync(sessionTypeId.Value, start, end, ct),
                };
                return Results.Ok(response);
            })
            .Produces<ListStudioSlotsResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books the signed-in shopper into a class
/// </summary>
public class StudioClassBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (ClassBookingRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var booking = await studio.BookClassAsync(StudioEndpointHelpers.UserName(user), request, ct);
                return StudioEndpointHelpers.Booking(booking, booking.AlreadyBooked, $"api/studio/class-bookings/{booking.BookingId}");
            })
            .Produces<ClassBookingDto>(StatusCodes.Status201Created)
            .Produces<ClassBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books the signed-in shopper into a one-to-one session
/// </summary>
public class StudioAppointmentBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (AppointmentBookingRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var booking = await studio.BookAppointmentAsync(StudioEndpointHelpers.UserName(user), request, ct);
                return StudioEndpointHelpers.Booking(booking, booking.AlreadyBooked, $"api/studio/appointment-bookings/{booking.BookingId}");
            })
            .Produces<AppointmentBookingDto>(StatusCodes.Status201Created)
            .Produces<AppointmentBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's class booking (cancelling it again also succeeds)
/// </summary>
public class StudioClassBookingDeleteEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.CancelClassBookingAsync(StudioEndpointHelpers.UserName(user), bookingId, ct)))
            .Produces<CancelBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's one-to-one session booking (cancelling it again also succeeds)
/// </summary>
public class StudioAppointmentBookingDeleteEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.CancelAppointmentBookingAsync(StudioEndpointHelpers.UserName(user), bookingId, ct)))
            .Produces<CancelBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Lists every trainer with all their contact details (front desk)
/// </summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioTrainersResponse(Guid.NewGuid()) { Trainers = await studio.ListTrainersAsync(ct) };
                return Results.Ok(response);
            })
            .Produces<ListStudioTrainersResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Places a shopper in any class in the timetable (front desk)
/// </summary>
public class StudioAdminClassBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = StudioEndpointHelpers.SHOPPER_SCHEME)] async
            (AdminClassBookingRequest request, StudioService studio, CancellationToken ct) =>
            {
                var shopper = await studio.ResolveShopperUserNameAsync(request.Shopper);
                var booking = await studio.BookClassForShopperAsync(shopper, request.ClassId, request, shopperRules: false, ct);
                return StudioEndpointHelpers.Booking(booking, booking.AlreadyBooked, $"api/studio/class-bookings/{booking.BookingId}");
            })
            .Produces<ClassBookingDto>(StatusCodes.Status201Created)
            .Produces<ClassBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
