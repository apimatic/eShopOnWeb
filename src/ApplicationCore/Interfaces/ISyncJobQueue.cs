using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Hands off sync work to a background worker so <c>POST .../sync</c> can return before the sync
/// finishes. The producing side only enqueues; the hosted worker owns the consuming side.
/// </summary>
public interface ISyncJobQueue
{
    void Enqueue(int syncId);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
