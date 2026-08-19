using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Hands off a started sync to the background worker so the HTTP request can return immediately.
/// </summary>
public interface ISupplierSyncQueue
{
    ValueTask EnqueueAsync(int syncId, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
