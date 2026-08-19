using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

/// <summary>
/// Starts a sync of a supplier's product listing. Returns immediately with a sync id; the actual
/// read-and-import runs in the background. Operator-only.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class StartSupplierSyncEndpoint : EndpointBaseAsync
    .WithRequest<StartSupplierSyncRequest>
    .WithActionResult<StartSupplierSyncResponse>
{
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly ISyncQueue _syncQueue;

    public StartSupplierSyncEndpoint(
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository,
        ISyncQueue syncQueue)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _syncQueue = syncQueue;
    }

    [HttpPost("api/catalog/suppliers/{supplierId}/sync")]
    [SwaggerOperation(
        Summary = "Starts a sync of a supplier's product listing",
        Description = "Queues a background sync that reads the supplier's listing and imports its products",
        OperationId = "catalog.suppliers.sync",
        Tags = new[] { "CatalogSupplierEndpoints" })
    ]
    public override async Task<ActionResult<StartSupplierSyncResponse>> HandleAsync(
        StartSupplierSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return NotFound($"Supplier {request.SupplierId} was not found.");
        }

        var sync = new CatalogSync(supplier.Id);
        sync = await _syncRepository.AddAsync(sync, cancellationToken);

        await _syncQueue.EnqueueAsync(sync.Id, cancellationToken);

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = supplier.Id,
            Status = sync.Status.ToString()
        };

        return Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
