using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public class AdminClassBookingRequest
{
    /// <summary>The shopper's eShop user name (e-mail address) or user id.</summary>
    public string? Shopper { get; set; }

    public int? ClassId { get; set; }
}

/// <summary>
/// Lists every trainer with all their contact details (front-desk operators)
/// </summary>
public class TrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListTrainersAsync(ct)))
            .Produces<List<TrainerDto>>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Places a shopper in any class in the timetable (front-desk operators)
/// </summary>
public class AdminBookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminClassBookingRequest? request, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.AdminBookClassAsync(request?.Shopper, request?.ClassId, ct)))
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }
}
