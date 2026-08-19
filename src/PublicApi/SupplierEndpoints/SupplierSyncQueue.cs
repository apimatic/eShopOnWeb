using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// In-process, unbounded hand-off queue of sync ids from the request that starts a sync to the
/// background worker that runs it. A single reader (the worker) processes syncs sequentially,
/// which also avoids brand/type get-or-create races within the import.
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
