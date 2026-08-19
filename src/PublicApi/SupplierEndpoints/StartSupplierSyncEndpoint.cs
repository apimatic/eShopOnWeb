using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Starts a sync of a supplier's product listing. The sync runs in the background, so this returns
/// as soon as the sync is recorded and queued. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, StartSupplierSyncRequest, ISupplierCatalogSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId}/sync",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId, ISupplierCatalogSyncService syncService) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), syncService);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(StartSupplierSyncRequest request, ISupplierCatalogSyncService syncService)
    {
        var syncId = await syncService.StartSyncAsync(request.SupplierId);
        if (syncId is null)
        {
            return Results.NotFound($"No supplier with id {request.SupplierId} exists.");
        }

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = syncId.Value,
            SupplierId = request.SupplierId
        };

        return Results.Accepted($"api/catalog/syncs/{syncId.Value}", response);
    }
}
