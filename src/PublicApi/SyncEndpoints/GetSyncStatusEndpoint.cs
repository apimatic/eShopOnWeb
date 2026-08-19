using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SyncEndpoints;

/// <summary>
/// Reports the status and outcome of one sync: whether it is still running or finished, and how many
/// products were found versus imported. Operator-only.
/// </summary>
public class GetSyncStatusEndpoint : IEndpoint<IResult, GetSyncStatusRequest, IReadRepository<CatalogSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, IReadRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(new GetSyncStatusRequest(syncId), syncRepository);
            })
            .Produces<GetSyncStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SyncEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSyncStatusRequest request, IReadRepository<CatalogSync> syncRepository)
    {
        var sync = await syncRepository.GetByIdAsync(request.SyncId);
        if (sync is null)
        {
            return Results.NotFound();
        }

        var response = new GetSyncStatusResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt,
            Error = sync.Error
        };

        return Results.Ok(response);
    }
}
