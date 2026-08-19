using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// In-process implementation of <see cref="ISupplierSyncQueue"/> backed by an unbounded channel.
/// Registered as a singleton so producers (API requests) and the single background consumer share it.
/// </summary>
public sealed class SupplierSyncQueue : ISupplierSyncQueue
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
