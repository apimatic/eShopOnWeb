using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Executes a supplier catalog sync: reads the supplier's listing via Firecrawl and imports the
/// products it finds into the store's own catalog, matching each product by the supplier's own
/// identifier so a re-run updates rather than duplicates.
/// </summary>
public interface ISupplierCatalogSyncService
{
    /// <summary>Runs the sync identified by <paramref name="syncId"/> to completion.</summary>
    Task ExecuteSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
