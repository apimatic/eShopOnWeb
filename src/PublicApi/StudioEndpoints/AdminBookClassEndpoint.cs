using System;
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

/// <summary>Places a shopper in any class of the timetable (front desk only).</summary>
public class AdminBookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminBookClassRequest request, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.AdminBookClassAsync(request, ct);
                return Results.Json(booking, statusCode: created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
            })
            .Produces<ClassBookingDto>(StatusCodes.Status201Created)
            .Produces<ClassBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
