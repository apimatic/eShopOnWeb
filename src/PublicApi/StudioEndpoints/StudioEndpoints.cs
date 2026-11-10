using System;
using System.Collections.Generic;
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

/// <summary>Shared plumbing of the studio endpoints.</summary>
internal static class StudioApi
{
    public const string TAG = "StudioEndpoints";
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;
    public const string ADMIN_ROLE = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    /// <summary>The caller's identity comes from the JWT, never from the request.</summary>
    public static string? UserName(ClaimsPrincipal user) => user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);

    /// <summary>Runs a studio operation; a request the app cannot carry out becomes a 4xx answer with its reason.</summary>
    public static async Task<IResult> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (StudioException ex)
        {
            return Problem(ex.StatusCode, ex.Message);
        }
    }

    public static IResult Problem(int status, string reason) =>
        Results.Json(new StudioProblem(status, reason), statusCode: status, contentType: "application/problem+json");
}

public record StudioProblem(int Status, string Message)
{
    public string Title => Message;
    public string Detail => Message;
}

public class ListClassesResponse : BaseResponse
{
    public ListClassesResponse(Guid correlationId) : base(correlationId) { }
    public List<StudioClassDto> Classes { get; set; } = new();
}

public class ListSlotsResponse : BaseResponse
{
    public ListSlotsResponse(Guid correlationId) : base(correlationId) { }
    public List<StudioSlotDto> Slots { get; set; } = new();
}

public class ListTrainersResponse : BaseResponse
{
    public ListTrainersResponse(Guid correlationId) : base(correlationId) { }
    public List<StudioTrainerDto> Trainers { get; set; } = new();
}

/// <summary>Lists the classes a shopper can attend for a range of days (default: today and the following 7 days).</summary>
public class ListClassesEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = StudioApi.SCHEME)] async (string? from, string? to, StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(async () => new ListClassesResponse(Guid.NewGuid()) { Classes = await studio.ListClassesAsync(from, to, ct) }))
            .Produces<ListClassesResponse>()
            .WithTags(StudioApi.TAG);
    }
}

/// <summary>Lists the times a session of a type can start on each day of a range, whether or not a trainer is free.</summary>
public class ListSlotsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = StudioApi.SCHEME)] async (int? sessionTypeId, int? sessionType, string? from, string? to, StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(async () => new ListSlotsResponse(Guid.NewGuid())
                {
                    Slots = await studio.ListSlotsAsync(sessionTypeId ?? sessionType ?? 0, from, to, ct),
                }))
            .Produces<ListSlotsResponse>()
            .WithTags(StudioApi.TAG);
    }
}

/// <summary>Books the signed-in shopper into a class.</summary>
public class BookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = StudioApi.SCHEME)] async (BookClassRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(() => studio.BookClassAsync(StudioApi.UserName(user)!, request, ct)))
            .Produces<StudioBookingDto>()
            .WithTags(StudioApi.TAG);
    }
}

/// <summary>Books the signed-in shopper into a one-to-one session.</summary>
public class BookAppointmentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = StudioApi.SCHEME)] async (BookAppointmentRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(() => studio.BookAppointmentAsync(StudioApi.UserName(user)!, request, ct)))
            .Produces<StudioBookingDto>()
            .WithTags(StudioApi.TAG);
    }
}

/// <summary>Cancels the signed-in shopper's own class booking.</summary>
public class CancelClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioApi.SCHEME)] async (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(() => studio.CancelClassBookingAsync(StudioApi.UserName(user)!, bookingId, ct)))
            .Produces<StudioCancellationDto>()
            .WithTags(StudioApi.TAG);
    }
}

/// <summary>Cancels the signed-in shopper's own one-to-one session booking.</summary>
public class CancelAppointmentBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = StudioApi.SCHEME)] async (long bookingId, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(() => studio.CancelAppointmentBookingAsync(StudioApi.UserName(user)!, bookingId, ct)))
            .Produces<StudioCancellationDto>()
            .WithTags(StudioApi.TAG);
    }
}

/// <summary>Lets front-desk operators (administrators) see every trainer with all their contact details.</summary>
public class AdminListTrainersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = StudioApi.ADMIN_ROLE, AuthenticationSchemes = StudioApi.SCHEME)] async (StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(async () => new ListTrainersResponse(Guid.NewGuid()) { Trainers = await studio.ListTrainersAsync(ct) }))
            .Produces<ListTrainersResponse>()
            .WithTags(StudioApi.TAG);
    }
}

/// <summary>Lets front-desk operators (administrators) place a shopper in any class in the timetable.</summary>
public class AdminBookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = StudioApi.ADMIN_ROLE, AuthenticationSchemes = StudioApi.SCHEME)] async (AdminBookClassRequest request, StudioService studio, CancellationToken ct) =>
                await StudioApi.RunAsync(() => studio.AdminBookClassAsync(request, ct)))
            .Produces<StudioBookingDto>()
            .WithTags(StudioApi.TAG);
    }
}
