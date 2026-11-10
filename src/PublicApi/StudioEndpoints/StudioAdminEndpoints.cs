using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Lists every trainer with all their contact details (front desk / administrators only)
/// </summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService studio, CancellationToken ct) => Results.Ok(await studio.ListTrainersAsync(ct)))
            .Produces<System.Collections.Generic.List<TrainerDto>>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Places a shopper in any class in the timetable (front desk / administrators only)
/// </summary>
public class StudioAdminBookClassEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminBookClassRequest request, UserManager<ApplicationUser> userManager, StudioService studio, CancellationToken ct) =>
            {
                if (request.ClassId is null)
                    throw new StudioException(400, "'classId' is required.");
                var shopper = await StudioShopperResolver.FromNameAsync(userManager, request.Shopper);
                return Results.Ok(await studio.BookClassForShopperAsync(shopper, request.ClassId.Value, ct));
            })
            .Produces<ClassBookingDto>()
            .WithTags("StudioEndpoints");
    }
}
