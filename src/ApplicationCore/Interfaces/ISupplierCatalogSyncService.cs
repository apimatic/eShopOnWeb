using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a supplier catalog sync: reads the supplier's listing via <see cref="ISupplierListingReader"/>
/// and imports each product into the store catalog, updating the <c>CatalogSync</c> record with
/// its status and outcome counts. Idempotent — re-running a sync updates the same catalog items
/// rather than creating duplicates.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
