using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a supplier catalog sync: reads the supplier's listing and matches every product found
/// into the store's own catalog, updating the sync record with the outcome.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
