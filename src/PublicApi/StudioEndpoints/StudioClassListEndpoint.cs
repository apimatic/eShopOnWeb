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
/// Lists the classes shoppers can attend
/// </summary>
public class StudioClassListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, StudioService studio, CancellationToken ct) =>
            {
                var classes = await studio.ListClassesAsync(StudioService.ParseDate(from, "from"), StudioService.ParseDate(to, "to"), ct);
                return Results.Ok(new ListStudioClassesResponse(System.Guid.NewGuid(), classes));
            })
            .WithTags("StudioEndpoints");
    }
}
