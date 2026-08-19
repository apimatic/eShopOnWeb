using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SupplierIntegration;

/// <summary>
/// An in-process, unbounded channel of sync ids waiting to be run by the background worker.
/// Registered as a singleton so the API endpoints and the hosted worker share one queue.
/// </summary>
public class SupplierSyncQueue : ISupplierSyncQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(syncId, cancellationToken);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
