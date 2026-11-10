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
/// Lists the times a session of a type can start
/// </summary>
public class StudioSlotListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? sessionTypeId, string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                var slots = await studio.ListSlotsAsync(sessionTypeId ?? 0, StudioService.ParseDate(from, "from"), StudioService.ParseDate(to, "to"), ct);
                return Results.Ok(new ListStudioSlotsResponse(System.Guid.NewGuid(), slots));
            })
            .WithTags("StudioEndpoints");
    }
}
