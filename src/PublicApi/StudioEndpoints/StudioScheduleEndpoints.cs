using System;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Lists the studio's classes a shopper can attend (default: today and the following 7 days)
/// </summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListClassesAsync(from, to, ct)))
            .Produces<System.Collections.Generic.List<ClassDto>>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Lists the times a session of a type can start on each day, whether or not a trainer is free
/// </summary>
public class StudioSlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? sessionTypeId, DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct) =>
            {
                if (sessionTypeId is null)
                    throw new StudioException(400, "'sessionTypeId' is required.");
                return Results.Ok(await studio.ListSlotsAsync(sessionTypeId.Value, from, to, ct));
            })
            .Produces<System.Collections.Generic.List<SlotDto>>()
            .WithTags("StudioEndpoints");
    }
}
