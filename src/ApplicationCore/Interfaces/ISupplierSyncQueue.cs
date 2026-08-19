using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A background work queue for supplier catalog syncs. Starting a sync enqueues its id; a
/// background worker dequeues and runs it, so the start request can return before the sync
/// finishes.
/// </summary>
public interface ISupplierSyncQueue
{
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
