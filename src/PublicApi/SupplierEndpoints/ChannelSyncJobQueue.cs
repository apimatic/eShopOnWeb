using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// In-process, unbounded sync-job queue backed by a <see cref="Channel{T}"/>. Registered as a
/// singleton so the endpoint (producer) and the hosted worker (single consumer) share one queue.
/// </summary>
public sealed class ChannelSyncJobQueue : ISyncJobQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(int syncId) => _channel.Writer.TryWrite(syncId);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
