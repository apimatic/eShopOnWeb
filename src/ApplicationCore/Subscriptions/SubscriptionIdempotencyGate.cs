using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Per-user gate so concurrent subscribe requests for the same shopper serialize
/// within a process. Maxio unique references cover the remaining race window.
/// </summary>
public sealed class SubscriptionIdempotencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public SemaphoreSlim ForUser(string userId) =>
        _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
}
