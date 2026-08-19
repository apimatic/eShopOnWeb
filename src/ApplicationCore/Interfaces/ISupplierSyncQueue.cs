using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A background work queue of sync runs. Starting a sync only enqueues its id and
/// returns immediately; a background worker drains the queue and runs each sync,
/// so the HTTP call that started it does not have to wait for it to finish.
/// </summary>
public interface ISupplierSyncQueue
{
    /// <summary>Enqueues a sync run for background processing.</summary>
    ValueTask EnqueueAsync(Guid syncId, CancellationToken cancellationToken = default);

    /// <summary>Awaits and removes the next queued sync run.</summary>
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
