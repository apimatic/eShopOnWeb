using System;
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
/// Lists classes, session times (shoppers) and trainers (front-desk operators)
/// </summary>
public class StudioQueryEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct) =>
            {
                var response = new ClassListResponse(Guid.NewGuid())
                {
                    Classes = await studio.ListClassesAsync(from, to, ct)
                };
                return Results.Ok(response);
            })
            .Produces<ClassListResponse>()
            .WithTags("StudioEndpoints");

        // Session times are listed whether or not a trainer is free at each time.
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int sessionTypeId, DateOnly from, DateOnly to, StudioService studio, CancellationToken ct) =>
            {
                var response = new SlotListResponse(Guid.NewGuid())
                {
                    SessionTypeId = sessionTypeId,
                    Slots = await studio.ListSlotsAsync(sessionTypeId, from, to, ct)
                };
                return Results.Ok(response);
            })
            .Produces<SlotListResponse>()
            .WithTags("StudioEndpoints");

        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService studio, CancellationToken ct) =>
            {
                var response = new TrainerListResponse(Guid.NewGuid())
                {
                    Trainers = await studio.ListTrainersAsync(ct)
                };
                return Results.Ok(response);
            })
            .Produces<TrainerListResponse>()
            .WithTags("StudioEndpoints");
    }
}
