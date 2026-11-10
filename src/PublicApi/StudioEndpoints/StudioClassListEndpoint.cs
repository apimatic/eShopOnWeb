using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public record StudioClassListRequest(string? From, string? To);

/// <summary>
/// List the studio's classes shoppers can attend
/// </summary>
public class StudioClassListEndpoint : IEndpoint<IResult, StudioClassListRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, StudioService service, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioClassListRequest(from, to), service, ct);
            })
            .Produces<StudioClassListResponse>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioClassListRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public Task<IResult> HandleAsync(StudioClassListRequest request, StudioService service, CancellationToken ct) =>
        StudioResults.RunAsync(() => service.ListClassesAsync(request.From, request.To, ct));
}
