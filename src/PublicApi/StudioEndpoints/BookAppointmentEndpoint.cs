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

/// <summary>Books the signed-in shopper into a one-to-one session. Repeating the request returns the same booking.</summary>
public class BookAppointmentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BookAppointmentRequest request, ClaimsPrincipal user, StudioService studio, CancellationToken ct) =>
            {
                var (booking, created) = await studio.BookAppointmentAsync(user, request, ct);
                return Results.Json(booking, statusCode: created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
            })
            .Produces<AppointmentBookingDto>(StatusCodes.Status201Created)
            .Produces<AppointmentBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
