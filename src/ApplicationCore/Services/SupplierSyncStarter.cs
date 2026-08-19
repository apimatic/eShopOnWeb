using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierSyncStarter : ISupplierSyncStarter
{
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly ISupplierSyncQueue _queue;

    public SupplierSyncStarter(
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository,
        ISupplierSyncQueue queue)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _queue = queue;
    }

    public async Task<CatalogSync?> StartAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            return null;
        }

        var sync = new CatalogSync(supplierId);
        sync = await _syncRepository.AddAsync(sync, cancellationToken);

        // Persisted as Pending before returning; the background worker moves it to Running.
        await _queue.EnqueueAsync(sync.Id, cancellationToken);
        return sync;
    }
}
