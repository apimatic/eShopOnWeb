using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Returns the status and outcome of a supplier catalog sync: whether it is still running or has
/// finished, and how many products it found versus imported. Restricted to administrators.
/// </summary>
public class GetSyncStatusEndpoint : IEndpoint<IResult, GetSyncStatusRequest, ISupplierCatalogSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId:int}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, ISupplierCatalogSyncService syncService) =>
            {
                return await HandleAsync(new GetSyncStatusRequest(syncId), syncService);
            })
            .Produces<GetSyncStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSyncStatusRequest request, ISupplierCatalogSyncService syncService)
    {
        var sync = await syncService.GetSyncAsync(request.SyncId);
        if (sync is null)
        {
            return Results.NotFound($"Sync {request.SyncId} was not found.");
        }

        var response = new GetSyncStatusResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            CreatedAt = sync.CreatedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt,
            ErrorMessage = sync.ErrorMessage
        };

        return Results.Ok(response);
    }
}
