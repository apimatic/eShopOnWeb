using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A queue of supplier syncs waiting to run. The API enqueues a sync id and returns immediately; a
/// background worker dequeues and processes them one at a time.
/// </summary>
public interface IBackgroundSyncQueue
{
    ValueTask QueueSyncAsync(int syncId, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
