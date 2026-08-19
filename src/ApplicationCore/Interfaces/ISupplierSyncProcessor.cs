using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Executes one supplier sync end-to-end: read the supplier's listing, then match every product
/// into the store's own catalog, updating the sync's status and counts as it goes.
/// </summary>
public interface ISupplierSyncProcessor
{
    Task ProcessAsync(int syncId, CancellationToken cancellationToken = default);
}
