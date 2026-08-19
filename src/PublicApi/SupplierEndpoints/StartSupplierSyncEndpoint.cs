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
/// Starts a sync of a supplier's product listing. Returns immediately (202 Accepted) with the
/// sync id; the actual work runs on a background worker. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint
    : IEndpoint<IResult, StartSupplierSyncRequest, IRepository<Supplier>, IRepository<CatalogSync>>
{
    private readonly ICatalogSyncQueue _syncQueue;

    public StartSupplierSyncEndpoint(ICatalogSyncQueue syncQueue)
    {
        _syncQueue = syncQueue;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId:int}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId, IRepository<Supplier> supplierRepository, IRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), supplierRepository, syncRepository);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(
        StartSupplierSyncRequest request,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository)
    {
        var supplier = await supplierRepository.GetByIdAsync(request.SupplierId);
        if (supplier is null)
        {
            return Results.NotFound($"Supplier {request.SupplierId} was not found.");
        }

        var sync = new CatalogSync(supplier.Id);
        sync = await syncRepository.AddAsync(sync);

        // Hand off to the background worker; this call returns without waiting for the sync to finish.
        _syncQueue.Enqueue(sync.Id);

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = supplier.Id,
            Status = sync.Status.ToString()
        };

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
