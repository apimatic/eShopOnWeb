using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Process-wide per-key locks, used to serialize customer/subscription
/// enrollment so concurrent requests for the same user (double-clicks)
/// cannot race past the existence checks and create duplicates.
/// Registered as a singleton; scoped services must not own these.
/// </summary>
public sealed class MaxioUserLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SemaphoreSlim GetLock(string key) =>
        _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
}
