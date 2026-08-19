using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IBackgroundSyncQueue _syncQueue;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository,
        IBackgroundSyncQueue syncQueue,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _syncQueue = syncQueue;
        _logger = logger;
    }

    public async Task<int?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            return null;
        }

        // Persist the sync before queueing it so the background worker (which runs in its own scope
        // and DbContext) can read it, and so GET /syncs/{id} can report it immediately.
        var sync = new CatalogSync(supplier.Id);
        sync = await _syncRepository.AddAsync(sync, cancellationToken);

        await _syncQueue.QueueSyncAsync(sync.Id, cancellationToken);

        _logger.LogInformation("Queued catalog sync {0} for supplier {1} ({2}).",
            sync.Id, supplier.Id, supplier.Name);

        return sync.Id;
    }
}
