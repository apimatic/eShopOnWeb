using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application service for the operator-facing supplier-sync use cases. Registering a supplier and
/// reading a sync are plain repository reads/writes; <em>starting</em> a sync also has to create the
/// run and hand it to the background queue, which this service encapsulates.
/// </summary>
public interface ISupplierSyncService
{
    /// <summary>
    /// Creates a queued sync for the supplier and enqueues it for background processing. Returns
    /// <c>null</c> when the supplier does not exist.
    /// </summary>
    Task<StartSyncResult?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default);
}

/// <summary>Identifies a newly-queued sync.</summary>
public sealed class StartSyncResult
{
    public int SyncId { get; }
    public int SupplierId { get; }
    public string Status { get; }

    public StartSyncResult(int syncId, int supplierId, string status)
    {
        SyncId = syncId;
        SupplierId = supplierId;
        Status = status;
    }
}
