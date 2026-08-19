using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>Channel-backed implementation of <see cref="ISupplierSyncQueue"/> (registered as a singleton).</summary>
public sealed class SupplierSyncQueue : ISupplierSyncQueue
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
