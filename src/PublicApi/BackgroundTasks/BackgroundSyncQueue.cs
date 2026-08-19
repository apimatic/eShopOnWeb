using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.BackgroundTasks;

/// <summary>
/// In-process queue of supplier sync ids, backed by an unbounded channel. Registered as a singleton
/// so the API (producer) and the hosted worker (consumer) share one queue.
/// </summary>
public class BackgroundSyncQueue : IBackgroundSyncQueue
{
    private readonly Channel<int> _queue = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public async ValueTask QueueSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        await _queue.Writer.WriteAsync(syncId, cancellationToken);
    }

    public async ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
