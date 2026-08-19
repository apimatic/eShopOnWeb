using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a supplier catalog sync: reads the supplier's listing and matches every product into
/// the store's own catalog, creating or updating catalog items and recording the outcome on
/// the sync record.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
