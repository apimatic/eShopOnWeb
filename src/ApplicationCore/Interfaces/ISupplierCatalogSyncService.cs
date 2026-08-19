using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a supplier catalog sync: reads the supplier's listing via Firecrawl and matches every
/// product it finds into the store's own catalog, updating the sync record with the outcome.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
