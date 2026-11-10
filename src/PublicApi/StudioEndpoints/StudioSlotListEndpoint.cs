using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public record StudioSlotListRequest(string? SessionTypeId, string? From, string? To);

/// <summary>
/// List the times a one-to-one session can start, day by day
/// </summary>
public class StudioSlotListEndpoint : IEndpoint<IResult, StudioSlotListRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? sessionTypeId, string? from, string? to, StudioService service, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioSlotListRequest(sessionTypeId, from, to), service, ct);
            })
            .Produces<StudioSlotListResponse>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioSlotListRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public Task<IResult> HandleAsync(StudioSlotListRequest request, StudioService service, CancellationToken ct) =>
        StudioResults.RunAsync(() => service.ListSlotsAsync(request.SessionTypeId, request.From, request.To, ct));
}
