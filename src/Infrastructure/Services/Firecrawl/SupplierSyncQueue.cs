using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// In-process, unbounded queue of sync ids backed by a <see cref="Channel{T}"/>. Registered as a
/// singleton so the API endpoint (producer) and the background worker (consumer) share one queue.
/// </summary>
public class SupplierSyncQueue : ISupplierSyncQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(int syncId) => _channel.Writer.TryWrite(syncId);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}
