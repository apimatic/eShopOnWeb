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
/// Starts a sync of a supplier's product listing. Returns immediately (202) with the sync id; the
/// import runs in the background. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, StartSupplierSyncRequest, ISupplierCatalogSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId:int}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId, ISupplierCatalogSyncService service) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), service);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(StartSupplierSyncRequest request, ISupplierCatalogSyncService service)
    {
        // Throws SupplierNotFoundException (mapped to 404) when the supplier does not exist.
        var sync = await service.StartSyncAsync(request.SupplierId);

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString()
        };

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
