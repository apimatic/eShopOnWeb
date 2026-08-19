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
/// Starts a sync of a supplier's product listing. The sync runs in the background, so this
/// returns as soon as the sync has been queued.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, StartSupplierSyncRequest>
{
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<SupplierSync> _syncRepository;
    private readonly ISupplierSyncQueue _syncQueue;

    public StartSupplierSyncEndpoint(
        IRepository<Supplier> supplierRepository,
        IRepository<SupplierSync> syncRepository,
        ISupplierSyncQueue syncQueue)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _syncQueue = syncQueue;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId));
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(StartSupplierSyncRequest request)
    {
        var supplier = await _supplierRepository.GetByIdAsync(request.SupplierId);
        if (supplier is null)
        {
            return Results.NotFound();
        }

        var sync = await _syncRepository.AddAsync(new SupplierSync(supplier.Id));
        await _syncQueue.EnqueueAsync(sync.Id);

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = supplier.Id,
            Status = sync.Status.ToString()
        };

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
