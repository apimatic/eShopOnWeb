using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// An in-process hand-off between the endpoint that starts a sync and the background worker
/// that runs it, so that starting a sync does not have to wait for it to finish.
/// </summary>
public interface ISupplierSyncQueue
{
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
