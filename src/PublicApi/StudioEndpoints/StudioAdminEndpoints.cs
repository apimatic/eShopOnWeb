using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public class StudioTrainerListRequest
{
}

/// <summary>
/// Lists every trainer with all their contact details (front desk)
/// </summary>
public class StudioTrainerListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService service, CancellationToken cancellationToken) =>
            {
                return Results.Ok(await service.GetTrainersAsync(cancellationToken));
            })
            .Produces<IReadOnlyList<StudioTrainerDto>>()
            .WithTags("StudioEndpoints");
    }

}

/// <summary>
/// Places a shopper in any class of the timetable (front desk)
/// </summary>
public class StudioAdminClassBookingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminClassBookingRequest request, StudioService service, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Shopper))
                {
                    throw StudioException.BadRequest("'shopper' (the shopper's user name) is required.");
                }
                if (request.ClassId <= 0)
                {
                    throw StudioException.BadRequest("'classId' is required.");
                }

                var shopper = await userManager.FindByNameAsync(request.Shopper.Trim())
                    ?? await userManager.FindByEmailAsync(request.Shopper.Trim())
                    ?? throw StudioException.NotFound($"There is no shopper '{request.Shopper}'.");

                var result = await service.BookClassForShopperAsync(shopper.UserName!, request.ClassId, cancellationToken);
                return StudioCaller.Booking(result);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }

}
