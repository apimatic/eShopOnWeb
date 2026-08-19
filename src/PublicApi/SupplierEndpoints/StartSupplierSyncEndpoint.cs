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
/// Starts a sync of a supplier's product listing. The sync runs in the background; this call
/// returns as soon as the sync has been queued. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, StartSupplierSyncRequest, IRepository<SupplierSync>>
{
    private readonly IReadRepository<Supplier> _supplierRepository;
    private readonly ISupplierSyncQueue _syncQueue;

    public StartSupplierSyncEndpoint(IReadRepository<Supplier> supplierRepository, ISupplierSyncQueue syncQueue)
    {
        _supplierRepository = supplierRepository;
        _syncQueue = syncQueue;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int supplierId, IRepository<SupplierSync> syncRepository) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), syncRepository);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(StartSupplierSyncRequest request, IRepository<SupplierSync> syncRepository)
    {
        var response = new StartSupplierSyncResponse(request.CorrelationId());

        var supplier = await _supplierRepository.GetByIdAsync(request.SupplierId);
        if (supplier is null)
        {
            return Results.NotFound($"Supplier {request.SupplierId} was not found.");
        }

        var sync = new SupplierSync(supplier.Id);
        sync = await syncRepository.AddAsync(sync);

        await _syncQueue.EnqueueAsync(sync.Id);

        response.SyncId = sync.Id;
        response.SupplierId = supplier.Id;
        response.Status = sync.Status.ToString();

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
