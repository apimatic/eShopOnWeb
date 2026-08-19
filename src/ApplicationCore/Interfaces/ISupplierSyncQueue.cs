using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A hand-off point between the request that starts a sync and the background worker that runs
/// it. The request enqueues a sync id and returns; the worker dequeues and processes it. The
/// implementation lives in the host (PublicApi).
/// </summary>
public interface ISupplierSyncQueue
{
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
