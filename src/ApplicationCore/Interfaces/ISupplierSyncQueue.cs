using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A background work queue for supplier syncs. Starting a sync only needs to enqueue its id;
/// the actual reading and importing happens out-of-band so the HTTP call can return
/// immediately.
/// </summary>
public interface ISupplierSyncQueue
{
    ValueTask EnqueueAsync(Guid syncId, CancellationToken cancellationToken = default);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
