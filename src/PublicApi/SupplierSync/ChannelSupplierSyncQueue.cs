using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>
/// In-process, unbounded queue of pending sync ids, backed by <see cref="Channel{T}"/>.
/// Registered as a singleton so the starting endpoint and the background worker share it.
/// </summary>
public class ChannelSupplierSyncQueue : ISupplierSyncQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(syncId, cancellationToken);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}
