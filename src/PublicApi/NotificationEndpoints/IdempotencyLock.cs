using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class IdempotencyLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemaphoreSlim For(string key) => _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
}
