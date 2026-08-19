using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs one sync end-to-end: reads the supplier's listing, matches/imports each product into the
/// catalog, and records the outcome on the <c>CatalogSync</c>. Invoked by the background worker.
/// </summary>
public interface ISupplierSyncProcessor
{
    Task ProcessAsync(int syncId, CancellationToken cancellationToken = default);
}
