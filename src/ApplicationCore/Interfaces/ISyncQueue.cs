using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A hand-off between the endpoint that starts a sync and the background worker that runs it, so
/// that starting a sync returns immediately without waiting for the listing to be read.
/// </summary>
public interface ISyncQueue
{
    /// <summary>Queues a sync to be executed by the background worker.</summary>
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    /// <summary>Waits for and returns the next queued sync id.</summary>
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
