using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.CatalogSync;

/// <summary>
/// An unbounded in-process channel of sync ids awaiting processing. Registered as a singleton so
/// the sync endpoint (producer) and the background worker (consumer) share one queue.
/// </summary>
public class SupplierSyncQueue : ISupplierSyncQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(syncId, cancellationToken);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
