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

/// <summary>Books the signed-in shopper into a class. Repeating the request returns the same booking.</summary>
public class BookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookClassRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.BookClassAsync(user, request, ct);
                return Results.Json(booking, statusCode: created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
            })
            .Produces<ClassBookingDto>(StatusCodes.Status201Created)
            .Produces<ClassBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
