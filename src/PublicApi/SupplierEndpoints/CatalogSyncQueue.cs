using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// In-process, unbounded FIFO queue of catalog syncs waiting to run, backed by a
/// <see cref="Channel{T}"/>. The "start sync" endpoint enqueues a sync id and returns
/// immediately; <see cref="CatalogSyncBackgroundService"/> drains the queue.
/// </summary>
public class CatalogSyncQueue : ICatalogSyncQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public void Enqueue(int syncId)
    {
        // Unbounded channel: TryWrite always succeeds and never blocks the request thread.
        _channel.Writer.TryWrite(syncId);
    }

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
