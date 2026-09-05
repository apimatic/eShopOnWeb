using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Serializes subscription requests for the same user within a process. Maxio's
/// uniqueness token provides the corresponding protection across processes.
/// </summary>
public sealed class SubscriptionRequestCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemaphoreSlim ForUser(string userReference) =>
        _locks.GetOrAdd(userReference, static _ => new SemaphoreSlim(1, 1));
}
