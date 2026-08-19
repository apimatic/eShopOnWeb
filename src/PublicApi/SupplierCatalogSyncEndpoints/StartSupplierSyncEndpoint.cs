using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

/// <summary>
/// Starts a sync of a registered supplier's product listing. Returns immediately with the
/// sync id; the actual work runs in the background. Operator-only.
/// </summary>
public class StartSupplierSyncEndpoint
    : IEndpoint<IResult, StartSupplierSyncRequest, IReadRepository<Supplier>, IRepository<CatalogSync>>
{
    private readonly ISupplierSyncQueue _syncQueue;

    public StartSupplierSyncEndpoint(ISupplierSyncQueue syncQueue)
    {
        _syncQueue = syncQueue;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId:guid}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid supplierId, IReadRepository<Supplier> supplierRepository, IRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), supplierRepository, syncRepository);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .WithTags("SupplierCatalogSyncEndpoints");
    }

    public async Task<IResult> HandleAsync(
        StartSupplierSyncRequest request,
        IReadRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository)
    {
        var supplier = await supplierRepository.GetByIdAsync(request.SupplierId);
        if (supplier is null)
            return Results.NotFound($"Supplier {request.SupplierId} was not found.");

        var sync = new CatalogSync(supplier.Id);
        sync = await syncRepository.AddAsync(sync);

        await _syncQueue.EnqueueAsync(sync.Id);

        var response = new StartSupplierSyncResponse
        {
            SyncId = sync.Id,
            SupplierId = supplier.Id,
            Status = sync.Status.ToString()
        };

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
