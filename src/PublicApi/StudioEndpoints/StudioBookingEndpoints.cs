using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public class ClassBookingRequest : ClientDetailsRequest
{
    public int? ClassId { get; set; }
}

public class AppointmentBookingRequest : ClientDetailsRequest
{
    public int? SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time with its UTC offset, one of the times listed by /api/studio/slots.</summary>
    public string? StartTime { get; set; }

    /// <summary>Optionally a particular trainer.</summary>
    public long? StaffId { get; set; }

    /// <summary>Optionally a particular studio location.</summary>
    public int? LocationId { get; set; }
}

/// <summary>
/// Books a place in a class for the signed-in shopper
/// </summary>
public class BookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClassBookingRequest? request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.BookClassAsync(user.Identity?.Name, request?.ClassId, request, ct)))
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Books a one-to-one session for the signed-in shopper
/// </summary>
public class BookAppointmentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AppointmentBookingRequest? request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.BookAppointmentAsync(user.Identity?.Name, request?.SessionTypeId, request?.StartTime,
                    request?.StaffId, request?.LocationId, request, ct)))
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's own class booking
/// </summary>
public class CancelClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.CancelClassBookingAsync(user.Identity?.Name, bookingId, ct)))
            .Produces<CancellationDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Cancels the signed-in shopper's own one-to-one session booking
/// </summary>
public class CancelAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.CancelAppointmentBookingAsync(user.Identity?.Name, bookingId, ct)))
            .Produces<CancellationDto>()
            .WithTags("StudioEndpoints");
    }
}
