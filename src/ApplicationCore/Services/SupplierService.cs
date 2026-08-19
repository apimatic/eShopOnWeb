using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierService : ISupplierService
{
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly ISyncJobQueue _jobQueue;
    private readonly IAppLogger<SupplierService> _logger;

    public SupplierService(
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository,
        ISyncJobQueue jobQueue,
        IAppLogger<SupplierService> logger)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _jobQueue = jobQueue;
        _logger = logger;
    }

    public async Task<Supplier> RegisterSupplierAsync(string name, string productListingUrl, CancellationToken cancellationToken = default)
    {
        var supplier = new Supplier(name, productListingUrl);
        supplier = await _supplierRepository.AddAsync(supplier, cancellationToken);
        _logger.LogInformation($"Registered supplier {supplier.Id} ('{supplier.Name}') -> {supplier.ProductListingUrl}");
        return supplier;
    }

    public async Task<CatalogSync> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            throw new SupplierNotFoundException(supplierId);
        }

        var sync = new CatalogSync(supplierId);
        sync = await _syncRepository.AddAsync(sync, cancellationToken);

        // Hand off to the background worker so the HTTP call can return immediately.
        _jobQueue.Enqueue(sync.Id);
        _logger.LogInformation($"Queued sync {sync.Id} for supplier {supplierId}");
        return sync;
    }
}
