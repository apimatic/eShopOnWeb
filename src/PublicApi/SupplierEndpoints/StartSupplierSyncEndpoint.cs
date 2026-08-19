using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SupplierSync;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Starts a sync of a registered supplier's listing. Returns immediately with the sync id; the
/// crawl and import run in the background. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, int, IRepository<Supplier>, IRepository<CatalogSync>>
{
    private readonly ISupplierSyncQueue _queue;

    public StartSupplierSyncEndpoint(ISupplierSyncQueue queue)
    {
        _queue = queue;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId:int}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId, IRepository<Supplier> supplierRepository, IRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(supplierId, supplierRepository, syncRepository);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int supplierId,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository)
    {
        var supplier = await supplierRepository.GetByIdAsync(supplierId);
        if (supplier is null)
        {
            return Results.NotFound($"Supplier {supplierId} was not found.");
        }

        var sync = new CatalogSync(supplierId);
        sync = await syncRepository.AddAsync(sync);

        await _queue.EnqueueAsync(sync.Id);

        var response = new StartSupplierSyncResponse
        {
            SyncId = sync.Id,
            SupplierId = supplierId,
            Status = sync.Status.ToString()
        };

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
