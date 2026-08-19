using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Starts syncs of a supplier's listing. Starting a sync records it and hands the work off to run in
/// the background, so the caller does not wait for the listing to be read.
/// </summary>
public interface ISupplierCatalogSyncService
{
    /// <summary>
    /// Records a new sync for the supplier and queues it to run. Returns the new sync's id, or
    /// <c>null</c> when no supplier with the given id exists.
    /// </summary>
    Task<int?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default);
}
