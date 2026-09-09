using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Process-wide registry of per-user locks, shared across all requests
/// (the billing service itself is scoped, so it cannot hold the locks).
/// </summary>
public sealed class MaxioUserLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemaphoreSlim GetLockForKey(string key) =>
        _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
}
