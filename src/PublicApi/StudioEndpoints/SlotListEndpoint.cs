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

/// <summary>Lists the times a session of a type can start on each day of a range.</summary>
public class SlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? sessionTypeId, DateOnly? from, DateOnly? to, StudioService studio, CancellationToken ct) =>
                Results.Ok(await studio.ListSlotsAsync(sessionTypeId, from, to, ct)))
            .Produces<System.Collections.Generic.List<SlotDto>>()
            .WithTags("StudioEndpoints");
    }
}
