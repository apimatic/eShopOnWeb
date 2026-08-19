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
    private readonly IAppLogger<SupplierSyncService> _logger;

    public SupplierSyncService(
        IRepository<Supplier> supplierRepository,
        IRepository<SupplierSync> syncRepository,
        ISupplierSyncQueue syncQueue,
        IAppLogger<SupplierSyncService> logger)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _syncQueue = syncQueue;
        _logger = logger;
    }

    public async Task<Supplier> RegisterSupplierAsync(string name, string productListingUrl, CancellationToken cancellationToken = default)
    {
        var supplier = new Supplier(name, productListingUrl);
        supplier = await _supplierRepository.AddAsync(supplier, cancellationToken);
        _logger.LogInformation($"Registered supplier {supplier.Id} ('{supplier.Name}') listing at {supplier.ProductListingUrl}");
        return supplier;
    }

    public async Task<SupplierSync?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            return null;
        }

        var sync = new SupplierSync(supplierId);
        sync = await _syncRepository.AddAsync(sync, cancellationToken);

        await _syncQueue.EnqueueAsync(sync.Id, cancellationToken);
        _logger.LogInformation($"Queued sync {sync.Id} for supplier {supplierId}");

        return sync;
    }

    public async Task<SupplierSync?> GetSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        return await _syncRepository.GetByIdAsync(syncId, cancellationToken);
    }
}
