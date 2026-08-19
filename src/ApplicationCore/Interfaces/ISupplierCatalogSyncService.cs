using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates registering suppliers and running their catalog syncs: reading a supplier's
/// listing through Firecrawl and matching the products found into the store's own catalog.
/// </summary>
public interface ISupplierCatalogSyncService
{
    /// <summary>Registers a supplier by name and the URL of its product listing page.</summary>
    Task<Supplier> RegisterSupplierAsync(string name, string listingUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a pending sync for a supplier and schedules it to run in the background.
    /// Returns immediately with the created (not yet finished) sync, or null if the supplier does not exist.
    /// </summary>
    Task<CatalogSync?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default);

    /// <summary>Returns the current state and outcome of a sync, or null if it does not exist.</summary>
    Task<CatalogSync?> GetSyncAsync(int syncId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a previously created sync end-to-end: reads the supplier's listing and upserts the
    /// products into the catalog. Intended to be invoked by the background sync worker.
    /// </summary>
    Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
