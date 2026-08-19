using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a single supplier sync end-to-end: reads the supplier's listing via Firecrawl and
/// upserts every product found into the store's catalog, updating the sync's status and counts.
/// </summary>
public interface ISupplierCatalogImporter
{
    Task ProcessAsync(int syncId, CancellationToken cancellationToken = default);
}
