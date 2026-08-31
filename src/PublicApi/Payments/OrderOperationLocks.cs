using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class OrderOperationLocks
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
    public SemaphoreSlim For(int orderId) => _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
}
