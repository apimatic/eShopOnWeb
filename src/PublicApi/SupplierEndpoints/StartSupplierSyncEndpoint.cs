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
/// Starts a sync of a supplier's listing. Returns immediately; the sync runs in the background.
/// Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, StartSupplierSyncRequest, IRepository<CatalogSync>, IRepository<Supplier>>
{
    // The queue is a singleton, so it is safe to inject into the endpoint's constructor; the scoped
    // repositories are resolved per-request as route-delegate parameters instead.
    private readonly ISupplierSyncQueue _queue;

    public StartSupplierSyncEndpoint(ISupplierSyncQueue queue)
    {
        _queue = queue;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId:int}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId,
             IRepository<CatalogSync> syncRepository,
             IRepository<Supplier> supplierRepository) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), syncRepository, supplierRepository);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(
        StartSupplierSyncRequest request,
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository)
    {
        var supplier = await supplierRepository.GetByIdAsync(request.SupplierId);
        if (supplier is null)
        {
            return Results.NotFound($"Supplier {request.SupplierId} was not found.");
        }

        var sync = new CatalogSync(supplier.Id);
        sync = await syncRepository.AddAsync(sync);

        await _queue.EnqueueAsync(sync.Id);

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = supplier.Id,
            Status = sync.Status.ToString()
        };

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
