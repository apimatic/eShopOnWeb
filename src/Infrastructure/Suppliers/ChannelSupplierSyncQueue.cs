using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Suppliers;

/// <summary>
/// An unbounded in-process queue of sync ids backed by <see cref="System.Threading.Channels"/>.
/// Starting a sync writes its id here and returns immediately; the background worker reads ids off
/// this queue and processes them one at a time.
/// </summary>
public class ChannelSupplierSyncQueue : ISupplierSyncQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(syncId, cancellationToken);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}
