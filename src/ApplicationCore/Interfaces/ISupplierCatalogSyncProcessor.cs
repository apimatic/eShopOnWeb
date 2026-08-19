using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Carries out one queued sync: reads the supplier's listing and matches every product it finds into
/// the catalog, updating the sync's status and counts as it goes.
/// </summary>
public interface ISupplierCatalogSyncProcessor
{
    Task ProcessAsync(int syncId, CancellationToken cancellationToken = default);
}
