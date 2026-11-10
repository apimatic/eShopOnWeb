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

/// <summary>
/// Lists every trainer with all their contact details (front-desk operators only)
/// </summary>
public class StudioTrainerListEndpoint : IEndpoint<IResult, StudioRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService studio, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioRequest { Cancellation = ct }, studio);
            })
            .Produces<ListStudioTrainersResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(StudioRequest request, StudioService studio)
    {
        var response = new ListStudioTrainersResponse(request.CorrelationId());
        response.Trainers.AddRange(await studio.ListTrainersAsync(request.Cancellation));
        return Results.Ok(response);
    }
}

/// <summary>
/// Places a shopper in any class of the timetable (front-desk operators only)
/// </summary>
public class StudioAdminClassBookingEndpoint : IEndpoint<IResult, StudioAdminClassBookingRequest, StudioApi>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminClassBookingBody? body, StudioApi api) =>
            {
                return await HandleAsync(new StudioAdminClassBookingRequest { Body = body }, api);
            })
            .Produces<StudioBookingResponse>()
            .WithTags("StudioEndpoints");
    }

    public async Task<IResult> HandleAsync(StudioAdminClassBookingRequest request, StudioApi api)
    {
        if (request.Body is null || request.Body.ClassId <= 0)
        {
            throw StudioException.BadRequest("shopper and classId are required.");
        }

        var shopper = await api.FindShopperAsync(request.Body.Shopper);
        var booking = await api.Studio.AdminBookClassAsync(shopper, request.Body.ClassId, CancellationToken.None);
        return Results.Ok(new StudioBookingResponse(request.CorrelationId()) { Booking = booking });
    }
}
