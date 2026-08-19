using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>
/// In-process queue of catalog syncs waiting to run. The sync endpoint enqueues a sync id and
/// returns immediately; <see cref="SupplierSyncBackgroundService"/> drains the queue and runs
/// each sync on a background worker.
/// </summary>
public interface ISupplierSyncQueue
{
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
