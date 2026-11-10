using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Books the signed-in shopper into a class
/// </summary>
public class BookClassEndpoint : IEndpoint<IResult, StudioRequest<BookClassBody>, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookClassBody? body, ClaimsPrincipal user, StudioService studio) =>
            {
                return await HandleAsync(new StudioRequest<BookClassBody>(user, body ?? new BookClassBody()), studio);
            })
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioRequest<BookClassBody> request, StudioService studio) =>
        StudioResults.RunAsync(async () =>
            Results.Ok(await studio.BookClassAsync(StudioResults.RequireUser(request.UserName), request.Body.ClassId, request.Body)));
}

/// <summary>
/// Books the signed-in shopper into a one-to-one session
/// </summary>
public class BookAppointmentEndpoint : IEndpoint<IResult, StudioRequest<BookAppointmentBody>, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookAppointmentBody? body, ClaimsPrincipal user, StudioService studio) =>
            {
                return await HandleAsync(new StudioRequest<BookAppointmentBody>(user, body ?? new BookAppointmentBody()), studio);
            })
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioRequest<BookAppointmentBody> request, StudioService studio) =>
        StudioResults.RunAsync(async () =>
            Results.Ok(await studio.BookAppointmentAsync(StudioResults.RequireUser(request.UserName),
                request.Body.SessionTypeId, request.Body.StartTime, request.Body.TrainerId, request.Body)));
}

/// <summary>
/// Cancels the signed-in shopper's own class booking
/// </summary>
public class CancelClassBookingEndpoint : IEndpoint<IResult, StudioRequest<long>, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio) =>
            {
                return await HandleAsync(new StudioRequest<long>(user, bookingId), studio);
            })
            .Produces<CancelledBookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioRequest<long> request, StudioService studio) =>
        StudioResults.RunAsync(async () =>
            Results.Ok(await studio.CancelClassBookingAsync(StudioResults.RequireUser(request.UserName), request.Body)));
}

/// <summary>
/// Cancels the signed-in shopper's own one-to-one session booking
/// </summary>
public class CancelAppointmentBookingEndpoint : IEndpoint<IResult, StudioRequest<long>, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long bookingId, ClaimsPrincipal user, StudioService studio) =>
            {
                return await HandleAsync(new StudioRequest<long>(user, bookingId), studio);
            })
            .Produces<CancelledBookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioRequest<long> request, StudioService studio) =>
        StudioResults.RunAsync(async () =>
            Results.Ok(await studio.CancelAppointmentBookingAsync(StudioResults.RequireUser(request.UserName), request.Body)));
}
