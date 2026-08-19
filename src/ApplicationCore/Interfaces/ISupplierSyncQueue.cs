using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A simple in-process queue of sync ids handed off from the API endpoint to a background
/// worker, so that starting a sync can return immediately without waiting for it to finish.
/// </summary>
public interface ISupplierSyncQueue
{
    /// <summary>Queues a sync for background execution.</summary>
    void Enqueue(int syncId);

    /// <summary>Waits for and removes the next queued sync id.</summary>
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
