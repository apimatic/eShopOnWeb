using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Books the signed-in shopper into a class
/// </summary>
public class StudioClassBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClassBookingRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.BookClassAsync(user.Identity?.Name, request, ct);
                return created ? Results.Created($"api/studio/class-bookings/{booking.BookingId}", booking) : Results.Ok(booking);
            })
            .WithTags("StudioEndpoints");
    }
}
