using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierSyncService : ISupplierSyncService
{
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<SupplierSync> _syncRepository;
    private readonly ISupplierSyncQueue _syncQueue;

    public SupplierSyncService(
        IRepository<Supplier> supplierRepository,
        IRepository<SupplierSync> syncRepository,
        ISupplierSyncQueue syncQueue)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _syncQueue = syncQueue;
    }

    public async Task<StartSyncResult?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
            return null;

        var sync = await _syncRepository.AddAsync(new SupplierSync(supplierId), cancellationToken);

        await _syncQueue.EnqueueAsync(sync.Id, cancellationToken);

        return new StartSyncResult(sync.Id, supplierId, sync.Status.ToString());
    }
}
