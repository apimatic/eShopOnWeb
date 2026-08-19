using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A queue of catalog syncs waiting to run. The API endpoint enqueues a sync id and returns
/// immediately; a background worker dequeues and executes it. This is what lets the "start sync"
/// call return before the sync finishes.
/// </summary>
public interface ICatalogSyncQueue
{
    void Enqueue(int syncId);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
