using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Lists every trainer with their contact details (front desk)
/// </summary>
public class ListTrainersEndpoint : IEndpoint<IResult, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService studio) =>
            {
                return await HandleAsync(studio);
            })
            .Produces<ListTrainersResponse>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioService studio) =>
        StudioResults.RunAsync(async () =>
        {
            var response = new ListTrainersResponse();
            response.Trainers.AddRange(await studio.ListTrainersAsync());
            return Results.Ok(response);
        });
}

/// <summary>
/// Places a shopper in any class of the timetable (front desk)
/// </summary>
public class AdminBookClassEndpoint : IEndpoint<IResult, AdminBookClassBody, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminBookClassBody? body, StudioService studio) =>
            {
                return await HandleAsync(body ?? new AdminBookClassBody(), studio);
            })
            .Produces<BookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(AdminBookClassBody request, StudioService studio) =>
        StudioResults.RunAsync(async () =>
            Results.Ok(await studio.BookClassForShopperAsync(request.Shopper, request.ClassId, request)));
}
