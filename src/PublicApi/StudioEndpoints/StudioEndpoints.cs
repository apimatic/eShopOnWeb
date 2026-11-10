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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Lists the classes shoppers can attend.</summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListClassesAsync(from, to, ct)))
            .Produces<StudioClassDto[]>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Lists the times a session of a type can start, whether or not a trainer is free.</summary>
public class StudioSlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long? sessionTypeId, DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct) =>
            {
                if (sessionTypeId is null) throw StudioException.BadRequest("sessionTypeId is required.");
                return Results.Ok(await studio.ListSlotsAsync(sessionTypeId.Value, from, to, ct));
            })
            .Produces<StudioSlotDto[]>()
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
            (StudioClassBookingRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
                StudioService studio, CancellationToken ct) =>
            {
                if (request.ClassId is null) throw StudioException.BadRequest("classId is required.");
                var shopper = await StudioShopperResolver.FromPrincipalAsync(principal, users);
                return Results.Ok(await studio.BookClassAsync(shopper, request.ClassId.Value, request, false, ct));
            })
            .Produces<StudioBookingDto>()
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
            (StudioAppointmentBookingRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
                StudioService studio, CancellationToken ct) =>
            {
                if (request.SessionTypeId is null) throw StudioException.BadRequest("sessionTypeId is required.");
                var start = request.ParseStartTime();
                var shopper = await StudioShopperResolver.FromPrincipalAsync(principal, users);
                return Results.Ok(await studio.BookAppointmentAsync(shopper, request.SessionTypeId.Value, start,
                    request.StaffId, request.LocationId, request, ct));
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Cancels the signed-in shopper's own class booking.</summary>
public class StudioClassCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/class-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string bookingId, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
                StudioService studio, CancellationToken ct) =>
            {
                var shopper = await StudioShopperResolver.FromPrincipalAsync(principal, users);
                return Results.Ok(await studio.CancelClassBookingAsync(shopper, bookingId, ct));
            })
            .Produces<StudioCancellationDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Cancels the signed-in shopper's own one-to-one session booking.</summary>
public class StudioAppointmentCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string bookingId, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
                StudioService studio, CancellationToken ct) =>
            {
                var shopper = await StudioShopperResolver.FromPrincipalAsync(principal, users);
                return Results.Ok(await studio.CancelAppointmentBookingAsync(shopper, bookingId, ct));
            })
            .Produces<StudioCancellationDto>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Front-desk view of every trainer with all contact details.</summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListTrainersAsync(ct)))
            .Produces<StudioTrainerDto[]>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>Front-desk booking of a shopper into any class in the timetable.</summary>
public class StudioAdminClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioAdminClassBookingRequest request, UserManager<ApplicationUser> users,
                StudioService studio, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Shopper)) throw StudioException.BadRequest("shopper is required.");
                if (request.ClassId is null) throw StudioException.BadRequest("classId is required.");
                var shopper = await StudioShopperResolver.FromUserNameAsync(request.Shopper, users);
                return Results.Ok(await studio.BookClassAsync(shopper, request.ClassId.Value, request, true, ct));
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
