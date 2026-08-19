using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A hand-off point between the endpoint that starts a sync and the background worker that runs it.
/// Enqueuing lets <c>POST .../sync</c> return immediately with a sync id while the work happens later.
/// </summary>
public interface ISupplierSyncQueue
{
    /// <summary>Queues a sync (identified by its <see cref="Entities.SupplierAggregate.SupplierSync"/> id) to be processed.</summary>
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    /// <summary>Waits for and returns the next queued sync id. Used by the background worker.</summary>
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
