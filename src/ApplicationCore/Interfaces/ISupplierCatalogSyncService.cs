using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a single supplier-catalog sync end to end: reads the supplier's listing via Firecrawl
/// and imports the products it finds into the store's own catalog, updating the sync record
/// with the outcome.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task ExecuteAsync(int syncId, CancellationToken cancellationToken = default);
}
