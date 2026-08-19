using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Reports the status and outcome of a single supplier catalog sync. Operator-only.
/// </summary>
public class GetCatalogSyncEndpoint : IEndpoint<IResult, GetCatalogSyncRequest, IReadRepository<CatalogSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId:int}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, IReadRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(new GetCatalogSyncRequest(syncId), syncRepository);
            })
            .Produces<GetCatalogSyncResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(GetCatalogSyncRequest request, IReadRepository<CatalogSync> syncRepository)
    {
        var sync = await syncRepository.GetByIdAsync(request.SyncId);
        if (sync is null)
        {
            return Results.NotFound($"Sync {request.SyncId} was not found.");
        }

        var response = new GetCatalogSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt,
            ErrorMessage = sync.ErrorMessage
        };

        return Results.Ok(response);
    }
}
