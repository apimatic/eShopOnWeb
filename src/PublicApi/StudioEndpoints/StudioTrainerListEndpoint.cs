using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public record StudioTrainerListRequest();

/// <summary>
/// List every trainer with their contact details (administrators only)
/// </summary>
public class StudioTrainerListEndpoint : IEndpoint<IResult, StudioTrainerListRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/admin/trainers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StudioService service, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioTrainerListRequest(), service, ct);
            })
            .Produces<StudioTrainerListResponse>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioTrainerListRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public Task<IResult> HandleAsync(StudioTrainerListRequest request, StudioService service, CancellationToken ct) =>
        StudioResults.RunAsync(() => service.ListTrainersAsync(ct));
}
