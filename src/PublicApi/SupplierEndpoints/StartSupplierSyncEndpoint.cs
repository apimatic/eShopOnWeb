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
/// Starts a sync of a supplier's listing. Returns immediately with a sync id; the work runs in the
/// background. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, StartSupplierSyncRequest, ISupplierSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId, ISupplierSyncService supplierSyncService) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), supplierSyncService);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(StartSupplierSyncRequest request, ISupplierSyncService supplierSyncService)
    {
        var sync = await supplierSyncService.StartSyncAsync(request.SupplierId);
        if (sync is null)
        {
            return Results.NotFound($"Supplier {request.SupplierId} was not found.");
        }

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString()
        };

        // 202 Accepted: work has been queued, not completed. Location points at the status resource.
        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
