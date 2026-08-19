using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>
/// In-process background queue of supplier syncs, backed by an unbounded channel. Starting a
/// sync only enqueues its id; <see cref="SupplierSyncWorker"/> drains the queue and runs each
/// sync in its own service scope.
/// </summary>
public class SupplierSyncQueue : ISupplierSyncQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid syncId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(syncId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}
