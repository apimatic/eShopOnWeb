using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the supplier-catalog-sync feature: registering suppliers, starting syncs
/// (queued for background processing) and reading their status, plus the actual import work.
/// </summary>
public interface ISupplierCatalogSyncService
{
    /// <summary>Registers a supplier by name and the URL of its product-listing page.</summary>
    Task<Supplier> RegisterSupplierAsync(string name, string productListingUrl, CancellationToken cancellationToken = default);

    /// <summary>Returns the supplier, or null if no supplier has that id.</summary>
    Task<Supplier?> GetSupplierAsync(int supplierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a queued sync for the supplier and hands it to the background worker. Returns
    /// immediately without waiting for the import to finish.
    /// </summary>
    /// <exception cref="Exceptions.SupplierNotFoundException">No supplier has that id.</exception>
    Task<SupplierSync> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default);

    /// <summary>Returns the sync, or null if no sync has that id.</summary>
    Task<SupplierSync?> GetSyncAsync(int syncId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the sync end-to-end: reads the supplier's listing, then creates or updates a catalog
    /// item for every importable product, and records the outcome on the sync. Invoked by the
    /// background worker. Never throws — failures are recorded on the sync as
    /// <see cref="SyncStatus.Failed"/>.
    /// </summary>
    Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
