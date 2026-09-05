using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Serializes same-user signups in a host while the database uniqueness constraints cover shared storage.</summary>
public sealed class SubscriptionRequestLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemaphoreSlim For(string userId) => _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
}
