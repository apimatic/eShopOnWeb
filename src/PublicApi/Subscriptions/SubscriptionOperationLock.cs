using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionOperationLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SemaphoreSlim Get(string key) => _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
}
