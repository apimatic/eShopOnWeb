using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// An in-process, ordered hand-off of sync ids from the API request that starts a sync to the
/// background worker that runs it. Lets <c>POST /sync</c> return before the sync has finished.
/// </summary>
public interface ISupplierSyncQueue
{
    /// <summary>Queues a sync to be run by the background worker.</summary>
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    /// <summary>Waits for and returns the next queued sync id.</summary>
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
