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
/// Reports the status and outcome of a single supplier-catalog sync. Operator-only.
/// </summary>
public class GetSyncStatusEndpoint : IEndpoint<IResult, int, IRepository<CatalogSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId:int}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, IRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(syncId, syncRepository);
            })
            .Produces<GetSyncStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(int syncId, IRepository<CatalogSync> syncRepository)
    {
        var sync = await syncRepository.GetByIdAsync(syncId);
        if (sync is null)
        {
            return Results.NotFound($"Sync {syncId} was not found.");
        }

        var response = new GetSyncStatusResponse
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            RequestedAt = sync.RequestedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt,
            ErrorMessage = sync.ErrorMessage
        };

        return Results.Ok(response);
    }
}
