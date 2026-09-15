using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentOperationLock
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public SemaphoreSlim For(int key) => _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
}
