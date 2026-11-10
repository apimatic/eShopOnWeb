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
    public const string SHOPPER = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's eShop identity, taken from the JWT.</summary>
    public static string? UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public static IResult Booking(Microsoft.eShopWeb.PublicApi.BaseRequest request, StudioBookingDto booking, bool created)
    {
        var response = new StudioBookingResponse(request.CorrelationId(), booking);
        return created
            ? Results.Created($"api/studio/{(booking.Kind == "class" ? "class" : "appointment")}-bookings/{booking.BookingId}", response)
            : Results.Ok(response);
    }
}

/// <summary>Lists the classes a shopper can attend.</summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioClassesResponse(System.Guid.NewGuid())
                {
                    Classes = await studio.GetClassesAsync(from, to, ct)
                };
                return Results.Ok(response);
            })
            .Produces<ListStudioClassesResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Lists the times a session of a type can start.</summary>
public class StudioSlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? sessionType, string? sessionTypeId, string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioSlotsResponse(System.Guid.NewGuid())
                {
                    Slots = await studio.GetSlotsAsync(sessionType ?? sessionTypeId, from, to, ct)
                };
                return Results.Ok(response);
            })
            .Produces<ListStudioSlotsResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Books the signed-in shopper into a class.</summary>
public class StudioClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookStudioClassRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var userName = StudioAuth.UserName(user);
                if (userName is null)
                    return Results.Unauthorized();
                if (request.ClassId is null or <= 0)
                    throw StudioException.BadRequest("'classId' is required.");

                var (booking, created) = await studio.BookClassAsync(userName, request.ClassId.Value, request.ToDetails(), ct);
                return StudioAuth.Booking(request, booking, created);
            })
            .Produces<StudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Books the signed-in shopper into a one-to-one session.</summary>
public class StudioAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookStudioAppointmentRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var userName = StudioAuth.UserName(user);
                if (userName is null)
                    return Results.Unauthorized();
                if (request.SessionTypeId is null or <= 0)
                    throw StudioException.BadRequest("'sessionTypeId' is required.");
                if (request.StartTime is null)
                    throw StudioException.BadRequest("'startTime' is required (ISO-8601 with UTC offset).");

                var (booking, created) = await studio.BookAppointmentAsync(userName, request.SessionTypeId.Value,
                    request.StartTime.Value, request.TrainerId ?? request.StaffId, request.LocationId, request.ToDetails(), ct);
                return StudioAuth.Booking(request, booking, created);
            })
            .Produces<StudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Cancels the signed-in shopper's class booking.</summary>
public class StudioCancelClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId:long}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var userName = StudioAuth.UserName(user);
                if (userName is null)
                    return Results.Unauthorized();
                await studio.CancelClassBookingAsync(userName, bookingId, ct);
                return Results.Ok(new CancelStudioBookingResponse(System.Guid.NewGuid(), bookingId));
            })
            .Produces<CancelStudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Cancels the signed-in shopper's one-to-one session booking.</summary>
public class StudioCancelAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId:long}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var userName = StudioAuth.UserName(user);
                if (userName is null)
                    return Results.Unauthorized();
                await studio.CancelAppointmentBookingAsync(userName, bookingId, ct);
                return Results.Ok(new CancelStudioBookingResponse(System.Guid.NewGuid(), bookingId));
            })
            .Produces<CancelStudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Lists every trainer with contact details (administrators only).</summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService studio, CancellationToken ct) =>
            {
                var response = new ListStudioTrainersResponse(System.Guid.NewGuid())
                {
                    Trainers = await studio.GetTrainersAsync(ct)
                };
                return Results.Ok(response);
            })
            .Produces<ListStudioTrainersResponse>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Places a shopper in a class (administrators only).</summary>
public class StudioAdminClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminBookStudioClassRequest request, StudioService studio, CancellationToken ct) =>
            {
                var shopper = (request.Shopper ?? request.UserName ?? request.Email)?.Trim();
                if (string.IsNullOrEmpty(shopper))
                    throw StudioException.BadRequest("'shopper' is required.");
                if (request.ClassId is null or <= 0)
                    throw StudioException.BadRequest("'classId' is required.");

                var (booking, created) = await studio.AdminBookClassAsync(shopper, request.ClassId.Value, ct);
                return StudioAuth.Booking(request, booking, created);
            })
            .Produces<StudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }
}
