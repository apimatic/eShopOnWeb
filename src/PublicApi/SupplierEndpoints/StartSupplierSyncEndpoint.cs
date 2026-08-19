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
/// Starts a sync of a supplier's listing. The read/import runs in the background; this returns as soon
/// as the sync has been queued. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, int, ISupplierSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId, ISupplierSyncService syncService) =>
            {
                return await HandleAsync(supplierId, syncService);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(int supplierId, ISupplierSyncService syncService)
    {
        var result = await syncService.StartSyncAsync(supplierId);
        if (result is null)
            return Results.NotFound($"Supplier {supplierId} was not found.");

        var response = new StartSupplierSyncResponse
        {
            SyncId = result.SyncId,
            SupplierId = result.SupplierId,
            Status = result.Status
        };

        return Results.Accepted($"api/catalog/syncs/{result.SyncId}", response);
    }
}
