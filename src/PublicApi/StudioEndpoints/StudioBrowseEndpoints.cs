using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Lists the studio classes shoppers can attend for a range of days (today and the following 7 days by default)
/// </summary>
public class ClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListClassesAsync(from, to, ct)))
            .Produces<List<ClassListingDto>>()
            .WithTags("StudioEndpoints");
    }
}

/// <summary>
/// Lists the times a one-to-one session of a type can start on each day between two dates
/// </summary>
public class SlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? sessionTypeId, string? from, string? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListSlotsAsync(sessionTypeId, from, to, ct)))
            .Produces<List<SlotDto>>()
            .WithTags("StudioEndpoints");
    }
}
