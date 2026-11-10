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
/// Places a shopper in any class (administrators only)
/// </summary>
public class StudioAdminClassBookingCreateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminClassBookingRequest request, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.AdminBookClassAsync(request, ct);
                return created ? Results.Created($"api/studio/class-bookings/{booking.BookingId}", booking) : Results.Ok(booking);
            })
            .WithTags("StudioEndpoints");
    }
}
